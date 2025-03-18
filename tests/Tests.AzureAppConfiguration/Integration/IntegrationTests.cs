using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppConfiguration;
using Azure.ResourceManager.AppConfiguration.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    /// <summary>
    /// Integration tests for Azure App Configuration that connect to a real service.
    /// Creates a temporary App Configuration store for testing and deletes it after the tests are complete.
    /// Requires Azure credentials with appropriate permissions.
    /// NOTE: Before running these tests, execute the GetAzureSubscription.ps1 script to create appsettings.Secrets.json.
    /// </summary>
    [Trait("Category", "Integration")]
    [CollectionDefinition(nameof(IntegrationTests), DisableParallelization = true)]
    public class IntegrationTests : IAsyncLifetime
    {
        // Test constants
        private const string TestKeyPrefix = "IntegrationTest";
        private const string DefaultLocation = "swedensouth";
        private const string SubscriptionJsonPath = "appsettings.Secrets.json";

        // Resource tagging and cleanup constants
        private const string ResourceGroupNamePrefix = "appconfig-dotnetprovider-test-";
        private const string StoreNamePrefix = "integration-";
        private const string TestResourceTag = "TestResource";
        private const string CreatedByTag = "CreatedBy";
        private const int StaleResourceThresholdHours = 24; // Resources older than this are considered stale

        /// <summary>
        /// Class to hold test-specific key information
        /// </summary>
        private class TestContext
        {
            public string KeyPrefix { get; set; }
            public string SentinelKey { get; set; }
            public string FeatureFlagKey { get; set; }
        }

        // Client for direct manipulation of the store
        private ConfigurationClient _configClient;

        // Connection string for the store
        private string _connectionString;

        // Store management resources
        private ArmClient _armClient;
        private string _testStoreName;
        private string _testResourceGroupName;
        private AppConfigurationStoreResource _appConfigStore;
        private Uri _appConfigEndpoint;
        private ResourceGroupResource _resourceGroup;
        private string _subscriptionId;

        /// <summary>
        /// Gets a DefaultAzureCredential for authentication.
        /// </summary>
        private DefaultAzureCredential GetCredential()
        {
            return new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeSharedTokenCacheCredential = true
            });
        }

        /// <summary>
        /// Gets the current subscription ID by reading from the JSON file created by the PowerShell script.
        /// NOTE: The PowerShell script must be run manually before running the tests.
        /// </summary>
        private string GetCurrentSubscriptionId()
        {
            // Read the JSON file created by the script
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Integration", SubscriptionJsonPath);

            if (!File.Exists(jsonPath))
            {
                throw new InvalidOperationException($"Subscription JSON file not found at {jsonPath}. Run the GetAzureSubscription.ps1 script first.");
            }

            string jsonContent = File.ReadAllText(jsonPath);

            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            bool success = root.GetProperty("Success").GetBoolean();

            if (!success)
            {
                throw new InvalidOperationException(root.GetProperty("ErrorMessage").GetString());
            }

            return root.GetProperty("SubscriptionId").GetString();
        }

        /// <summary>
        /// Returns the connection string for connecting to the app configuration store.
        /// </summary>
        private string GetConnectionString()
        {
            return _connectionString;
        }

        /// <summary>
        /// Generate a timestamped, unique resource name with the given prefix
        /// </summary>
        private string GenerateTimestampedResourceName(string prefix)
        {
            // Format: prefix-yyyyMMddHHmm-randomGuid (trimmed to 20 chars)
            string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmm");
            string randomPart = Guid.NewGuid().ToString("N").Substring(0, 8);
            return $"{prefix}{timestamp}-{randomPart}";
        }

        /// <summary>
        /// Generates a deterministic resource group name based on machine information
        /// </summary>
        private string GetDeterministicResourceGroupName()
        {
            string machineName = Environment.MachineName.ToLowerInvariant();
            string machineHash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.Create()
                .ComputeHash(System.Text.Encoding.UTF8.GetBytes(machineName)))
                .Replace("/", "-").Replace("+", "-").Replace("=", "").Substring(0, 8);

            return $"{ResourceGroupNamePrefix}{machineHash}";
        }

        /// <summary>
        /// Cleans up only stale App Configuration stores, not resource groups
        /// </summary>
        private async Task CleanupStaleStores()
        {
            if (_resourceGroup == null) return;

            try
            {
                var stores = _resourceGroup.GetAppConfigurationStores();
                var staleTime = DateTime.UtcNow.AddHours(-StaleResourceThresholdHours);

                await foreach (var store in stores.GetAllAsync())
                {
                    // Only delete stores that:
                    // 1. Start with our test prefix
                    // 2. Have the TestResourceTag
                    // 3. Are older than the threshold
                    if (!store.Data.Name.StartsWith(StoreNamePrefix) ||
                        !store.Data.Tags.ContainsKey(TestResourceTag))
                    {
                        continue;
                    }

                    // Check if the store is a temporary test store
                    if (store.Data.Tags.ContainsKey("TemporaryStore") &&
                        store.Data.Tags["TemporaryStore"] == "true")
                    {
                        // If it has a creation time tag, check if it's stale
                        if (store.Data.Tags.TryGetValue("CreatedOn", out string createdOnStr) &&
                            DateTime.TryParse(createdOnStr, out DateTime createdOn))
                        {
                            if (createdOn < staleTime)
                            {
                                await store.DeleteAsync(WaitUntil.Started);
                            }
                        }
                        else
                        {
                            // If no creation time or it can't be parsed, use a heuristic
                            // based on the timestamp in the name
                            string name = store.Data.Name;
                            if (name.Length > StoreNamePrefix.Length + 12) // yyyyMMddHHmm format is 12 chars
                            {
                                string timeStampPart = name.Substring(StoreNamePrefix.Length, 12);
                                if (DateTime.TryParseExact(timeStampPart, "yyyyMMddHHmm",
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.None, out DateTime timestamp))
                                {
                                    if (timestamp < staleTime)
                                    {
                                        await store.DeleteAsync(WaitUntil.Started);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during stale store cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a temporary Azure App Configuration store and adds test data.
        /// </summary>
        public async Task InitializeAsync()
        {
            bool success = false;

            try
            {
                var credential = GetCredential();

                // Get the current subscription ID from the JSON file
                _subscriptionId = GetCurrentSubscriptionId();

                // Initialize Azure Resource Manager client
                _armClient = new ArmClient(credential);

                _testResourceGroupName = GetDeterministicResourceGroupName();

                SubscriptionResource subscription = _armClient.GetSubscriptions().Get(_subscriptionId);

                // Check if the resource group already exists
                bool resourceGroupExists = false;
                try
                {
                    _resourceGroup = subscription.GetResourceGroup(_testResourceGroupName);
                    // If we get here, the resource group exists
                    resourceGroupExists = true;
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Resource group doesn't exist, we'll create it
                    resourceGroupExists = false;
                }

                // Create the resource group if it doesn't exist
                if (!resourceGroupExists)
                {
                    var rgData = new ResourceGroupData(new AzureLocation(DefaultLocation));

                    // Add tags to identify this as a persistent test resource group
                    rgData.Tags.Add("PersistentTestResource", "true");
                    rgData.Tags.Add(CreatedByTag, "IntegrationTests");
                    rgData.Tags.Add("CreatedOn", DateTime.UtcNow.ToString("o"));

                    var rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(
                        WaitUntil.Completed, _testResourceGroupName, rgData);
                    _resourceGroup = rgLro.Value;
                }

                // Clean up any stale resources before creating new ones
                await CleanupStaleStores();

                // Create unique store name for this test run with timestamp
                _testStoreName = GenerateTimestampedResourceName(StoreNamePrefix);

                // Create the App Configuration store
                var storeData = new AppConfigurationStoreData(new AzureLocation(DefaultLocation), new AppConfigurationSku("free"));

                storeData.Tags.Add(TestResourceTag, "true");
                storeData.Tags.Add(CreatedByTag, "IntegrationTests");
                storeData.Tags.Add("TemporaryStore", "true");

                var createOperation = await _resourceGroup.GetAppConfigurationStores().CreateOrUpdateAsync(
                    WaitUntil.Completed,
                    _testStoreName,
                    storeData);

                _appConfigStore = createOperation.Value;
                _appConfigEndpoint = new Uri(_appConfigStore.Data.Endpoint);

                // Get the connection string for the store
                var accessKeys = _appConfigStore.GetKeysAsync();
                var primaryKey = await accessKeys.FirstOrDefaultAsync();

                if (primaryKey == null)
                {
                    throw new InvalidOperationException("Failed to retrieve access keys from App Configuration store.");
                }

                _connectionString = primaryKey.ConnectionString;

                // Initialize the configuration client with the connection string
                _configClient = new ConfigurationClient(_connectionString);

                success = true;
            }
            finally
            {
                if (!success)
                {
                    await CleanupAppConfigurationStore();
                }
            }
        }

        /// <summary>
        /// Deletes only the App Configuration store, not the resource group
        /// </summary>
        private async Task CleanupAppConfigurationStore()
        {
            if (_appConfigStore != null)
            {
                try
                {
                    Console.WriteLine($"Cleaning up test store: {_testStoreName}");
                    await _appConfigStore.DeleteAsync(WaitUntil.Completed);
                    _appConfigStore = null;
                    Console.WriteLine("App Configuration store cleanup completed successfully");
                }
                catch (Exception ex) when (
                    ex is RequestFailedException ||
                    ex is InvalidOperationException ||
                    ex is TaskCanceledException)
                {
                    Console.WriteLine($"Store cleanup failed: {ex.Message}.");
                }
            }
        }

        public async Task DisposeAsync()
        {
            await CleanupAppConfigurationStore();

            try
            {
                await CleanupStaleStores();
            }
            catch (Exception ex) when (
                ex is RequestFailedException ||
                ex is InvalidOperationException ||
                ex is TaskCanceledException ||
                ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"Error during stale store cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a unique prefix for test keys to ensure test isolation
        /// </summary>
        private string GetUniqueKeyPrefix(string testName)
        {
            // Use a combination of the test prefix and test method name to ensure uniqueness
            return $"{TestKeyPrefix}_{testName}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        /// <summary>
        /// Setup test-specific keys and settings
        /// </summary>
        private async Task<TestContext> SetupTestKeys(string testName)
        {
            string keyPrefix = GetUniqueKeyPrefix(testName);
            string sentinelKey = $"{keyPrefix}:Sentinel";
            string featureFlagKey = $".appconfig.featureflag/{keyPrefix}Feature";

            // Create test-specific settings
            var testSettings = new List<ConfigurationSetting>
            {
                new ConfigurationSetting($"{keyPrefix}:Setting1", "InitialValue1"),
                new ConfigurationSetting($"{keyPrefix}:Setting2", "InitialValue2"),
                new ConfigurationSetting(sentinelKey, "Initial"),
                ConfigurationModelFactory.ConfigurationSetting(
                    featureFlagKey,
                    @"{""id"":""" + keyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":false}",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8")
            };

            // Add test-specific settings to the store
            foreach (var setting in testSettings)
            {
                await _configClient.SetConfigurationSettingAsync(setting);
            }

            return new TestContext
            {
                KeyPrefix = keyPrefix,
                SentinelKey = sentinelKey,
                FeatureFlagKey = featureFlagKey
            };
        }

        [Fact]
        public async Task LoadConfiguration_RetrievesValuesFromAppConfiguration()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("BasicConfig");

            // Act
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");
                })
                .Build();

            // Assert
            Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]);
        }

        [Fact]
        public async Task RefreshAsync_UpdatesConfiguration_WhenSentinelKeyChanged()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("UpdatesConfig");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey, refreshAll: true)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);

            // Update values in the store
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(testContext.SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            await refresher.RefreshAsync();

            // Assert
            Assert.Equal("UpdatedValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
        }

        [Fact]
        public async Task RegisterAll_RefreshesAllKeys()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("RefreshesAllKeys");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");

                    // Use RegisterAll to refresh everything when sentinel changes
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.RegisterAll()
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]);

            // Update all values in the store
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting2", "UpdatedValue2"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            await refresher.RefreshAsync();

            // Assert
            Assert.Equal("UpdatedValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("UpdatedValue2", config[$"{testContext.KeyPrefix}:Setting2"]);
        }

        [Fact]
        public async Task RefreshAsync_SentinelKeyUnchanged()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("SentinelUnchanged");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");

                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);

            // Update data but not sentinel
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting1", "UpdatedValue1"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            await refresher.RefreshAsync();

            // Assert
            Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]); // Should not update
        }

        [Fact]
        public async Task RefreshAsync_RefreshesFeatureFlags_WhenConfigured()
        {
            var testContext = await SetupTestKeys("FeatureFlagRefresh");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    // Select the key prefix to include all test keys
                    options.Select($"{testContext.KeyPrefix}:*");

                    // Configure feature flags with the correct ID pattern
                    options.UseFeatureFlags(featureFlagOptions =>
                    {
                        featureFlagOptions.Select(testContext.KeyPrefix + "*");
                        featureFlagOptions.SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey)
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify the feature flag is disabled initially
            Assert.Equal("False", config[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);

            // Update the feature flag to enabled=true
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":true}",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            // Update the sentinel key to trigger refresh
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(testContext.SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            await refresher.RefreshAsync();

            // Assert
            Assert.Equal("True", config[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
        }

        [Fact]
        public async Task UseFeatureFlags_WithClientFiltersAndConditions()
        {
            var testContext = await SetupTestKeys("FeatureFlagFilters");

            // Create a feature flag with complex conditions
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{
                        ""id"": """ + testContext.KeyPrefix + @"Feature"",
                        ""description"": ""Test feature with filters"",
                        ""enabled"": true,
                        ""conditions"": {
                            ""client_filters"": [
                                {
                                    ""name"": ""Browser"",
                                    ""parameters"": {
                                        ""AllowedBrowsers"": [""Chrome"", ""Edge""]
                                    }
                                },
                                {
                                    ""name"": ""TimeWindow"",
                                    ""parameters"": {
                                        ""Start"": ""\/Date(" + DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds() + @")\/"",
                                        ""End"": ""\/Date(" + DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds() + @")\/""
                                    }
                                }
                            ]
                        }
                    }",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.UseFeatureFlags(featureFlagOptions =>
                    {
                        featureFlagOptions.Select(testContext.KeyPrefix + "*");
                    });
                })
                .Build();

            // Verify feature flag structure is loaded correctly
            Assert.Equal("Browser", config[$"FeatureManagement:{testContext.KeyPrefix}Feature:EnabledFor:0:Name"]);
            Assert.Equal("Chrome", config[$"FeatureManagement:{testContext.KeyPrefix}Feature:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config[$"FeatureManagement:{testContext.KeyPrefix}Feature:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("TimeWindow", config[$"FeatureManagement:{testContext.KeyPrefix}Feature:EnabledFor:1:Name"]);
        }

        [Fact]
        public async Task MultipleProviders_LoadAndRefresh()
        {
            var testContext1 = await SetupTestKeys("MultiProviderTest1");
            var testContext2 = await SetupTestKeys("MultiProviderTest2");
            IConfigurationRefresher refresher1 = null;
            IConfigurationRefresher refresher2 = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext1.KeyPrefix}:*");
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext1.SentinelKey, true)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher1 = options.GetRefresher();
                })
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext2.KeyPrefix}:*");
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext2.SentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher2 = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{testContext1.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue1", config[$"{testContext2.KeyPrefix}:Setting1"]);

            // Update values and sentinel keys
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext1.KeyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(testContext1.SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Refresh only the first provider
            await refresher1.RefreshAsync();

            // Assert: Only the first provider's values should be updated
            Assert.Equal("UpdatedValue1", config[$"{testContext1.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue1", config[$"{testContext2.KeyPrefix}:Setting1"]);
        }

        [Fact]
        public async Task FeatureFlag_WithVariants()
        {
            var testContext = await SetupTestKeys("FeatureFlagVariants");

            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":true}",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            // Create a feature flag with variants
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey + "WithVariants",
                    @"{
                        ""id"": """ + testContext.KeyPrefix + @"FeatureWithVariants"",
                        ""description"": ""Feature flag with variants"",
                        ""enabled"": true,
                        ""conditions"": { ""client_filters"": [] },
                        ""variants"": [
                            {
                                ""name"": ""LargeSize"",
                                ""configuration_value"": ""800px""
                            },
                            {
                                ""name"": ""MediumSize"",
                                ""configuration_value"": ""600px""
                            },
                            {
                                ""name"": ""SmallSize"",
                                ""configuration_value"": ""400px""
                            }
                        ],
                        ""allocation"": {
                            ""default_when_enabled"": ""MediumSize""
                        }
                    }",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.UseFeatureFlags(featureFlagOptions =>
                    {
                        featureFlagOptions.Select(testContext.KeyPrefix + "*");
                    });
                })
                .Build();

            // Verify variants are loaded correctly
            Assert.Equal("True", config[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
            Assert.Equal("LargeSize", config[$"feature_management:feature_flags:0:variants:0:name"]);
            Assert.Equal("800px", config[$"feature_management:feature_flags:0:variants:0:configuration_value"]);
            Assert.Equal("MediumSize", config[$"feature_management:feature_flags:0:variants:1:name"]);
            Assert.Equal("600px", config[$"feature_management:feature_flags:0:variants:1:configuration_value"]);
            Assert.Equal("SmallSize", config[$"feature_management:feature_flags:0:variants:2:name"]);
            Assert.Equal("400px", config[$"feature_management:feature_flags:0:variants:2:configuration_value"]);
            Assert.Equal("MediumSize", config[$"feature_management:feature_flags:0:allocation:default_when_enabled"]);
        }

        [Fact]
        public async Task JsonContentType_LoadsAndFlattensHierarchicalData()
        {
            var testContext = await SetupTestKeys("JsonContent");

            // Create a complex JSON structure
            string jsonKey = $"{testContext.KeyPrefix}:JsonConfig";
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    jsonKey,
                    @"{
                        ""database"": {
                            ""connection"": {
                                ""string"": ""Server=myserver;Database=mydb;User Id=sa;Password=mypassword;"",
                                ""timeout"": 30
                            },
                            ""retries"": 3,
                            ""enabled"": true
                        },
                        ""logging"": {
                            ""level"": ""Information"",
                            ""providers"": [""Console"", ""Debug"", ""EventLog""]
                        }
                    }",
                    contentType: "application/json"));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");
                })
                .Build();

            // Verify JSON was flattened properly
            Assert.Equal("Server=myserver;Database=mydb;User Id=sa;Password=mypassword;", config[$"{jsonKey}:database:connection:string"]);
            Assert.Equal("30", config[$"{jsonKey}:database:connection:timeout"]);
            Assert.Equal("3", config[$"{jsonKey}:database:retries"]);
            Assert.Equal("True", config[$"{jsonKey}:database:enabled"]);
            Assert.Equal("Information", config[$"{jsonKey}:logging:level"]);
            Assert.Equal("Console", config[$"{jsonKey}:logging:providers:0"]);
            Assert.Equal("Debug", config[$"{jsonKey}:logging:providers:1"]);
            Assert.Equal("EventLog", config[$"{jsonKey}:logging:providers:2"]);
        }

        [Fact]
        public async Task MethodOrderingDoesNotAffectConfiguration()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("MethodOrdering");

            // Add an additional feature flag for testing
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey + "_Ordering",
                    @"{
                        ""id"": """ + testContext.KeyPrefix + @"FeatureOrdering"",
                        ""description"": ""Test feature for ordering"",
                        ""enabled"": true,
                        ""conditions"": {
                            ""client_filters"": []
                        }
                    }",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            // Add a section-based setting
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{testContext.KeyPrefix}:Section1:Setting1", "SectionValue1"));

            // Create four different configurations with different method orderings
            var configurations = new List<IConfiguration>();
            IConfigurationRefresher refresher1 = null;
            IConfigurationRefresher refresher2 = null;
            IConfigurationRefresher refresher3 = null;
            IConfigurationRefresher refresher4 = null;

            // Configuration 1: Select -> ConfigureRefresh -> UseFeatureFlags
            var config1 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    options.UseFeatureFlags(featureFlagOptions =>
                    {
                        featureFlagOptions.Select(testContext.KeyPrefix + "*");
                    });

                    refresher1 = options.GetRefresher();
                })
                .Build();
            configurations.Add(config1);

            // Configuration 2: ConfigureRefresh -> Select -> UseFeatureFlags
            var config2 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    options.Select($"{testContext.KeyPrefix}:*");
                    options.UseFeatureFlags(featureFlagOptions =>
                    {
                        featureFlagOptions.Select(testContext.KeyPrefix + "*");
                    });

                    refresher2 = options.GetRefresher();
                })
                .Build();
            configurations.Add(config2);

            // Configuration 3: UseFeatureFlags -> Select -> ConfigureRefresh
            var config3 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.UseFeatureFlags(featureFlagOptions =>
                    {
                        featureFlagOptions.Select(testContext.KeyPrefix + "*");
                    });
                    options.Select($"{testContext.KeyPrefix}:*");
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher3 = options.GetRefresher();
                })
                .Build();
            configurations.Add(config3);

            // Configuration 4: UseFeatureFlags (with Select inside) -> ConfigureRefresh -> Select
            var config4 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.UseFeatureFlags(featureFlagOptions =>
                    {
                        featureFlagOptions.Select(testContext.KeyPrefix + "*");
                    });
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    options.Select($"{testContext.KeyPrefix}:*");

                    refresher4 = options.GetRefresher();
                })
                .Build();
            configurations.Add(config4);

            // Assert - Initial values should be the same across all configurations
            foreach (var config in configurations)
            {
                // Regular settings
                Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
                Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]);
                Assert.Equal("SectionValue1", config[$"{testContext.KeyPrefix}:Section1:Setting1"]);

                // Feature flags
                Assert.Equal("False", config[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
                Assert.Equal("True", config[$"FeatureManagement:{testContext.KeyPrefix}FeatureOrdering"]);
            }

            // Update values in the store
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{testContext.KeyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{testContext.KeyPrefix}:Section1:Setting1", "UpdatedSectionValue1"));

            // Update a feature flag
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{
                        ""id"": """ + testContext.KeyPrefix + @"Feature"",
                        ""description"": ""Updated test feature"",
                        ""enabled"": true,
                        ""conditions"": {
                            ""client_filters"": []
                        }
                    }",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            // Update the sentinel key to trigger refresh
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting(testContext.SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Refresh all configurations
            await refresher1.RefreshAsync();
            await refresher2.RefreshAsync();
            await refresher3.RefreshAsync();
            await refresher4.RefreshAsync();

            // Assert - Updated values should be the same across all configurations
            foreach (var config in configurations)
            {
                // Regular settings
                Assert.Equal("UpdatedValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
                Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]);
                Assert.Equal("UpdatedSectionValue1", config[$"{testContext.KeyPrefix}:Section1:Setting1"]);

                // Feature flags - first one should be updated to true
                Assert.Equal("True", config[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
                Assert.Equal("True", config[$"FeatureManagement:{testContext.KeyPrefix}FeatureOrdering"]);
            }
        }

        [Fact]
        public async Task RegisterWithRefreshAllAndRegisterAll_BehaveIdentically()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("RefreshEquivalency");

            // Add another feature flag for testing
            string secondFeatureFlagKey = $".appconfig.featureflag/{testContext.KeyPrefix}Feature2";
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    secondFeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature2"",""description"":""Second test feature"",""enabled"":false}",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            // Create two separate configuration builders with different refresh methods
            // First configuration uses Register with refreshAll: true
            IConfigurationRefresher refresher1 = null;
            var config1 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");
                    options.UseFeatureFlags(featureFlagOptions =>
                    {
                        featureFlagOptions.Select(testContext.KeyPrefix + "*");
                    });
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey, refreshAll: true)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher1 = options.GetRefresher();
                })
                .Build();

            // Second configuration uses RegisterAll()
            IConfigurationRefresher refresher2 = null;
            var config2 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");
                    options.UseFeatureFlags(featureFlagOptions =>
                    {
                        featureFlagOptions.Select(testContext.KeyPrefix + "*");
                    });
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.RegisterAll()
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher2 = options.GetRefresher();
                })
                .Build();

            // Verify initial values for both configurations
            Assert.Equal("InitialValue1", config1[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config1[$"{testContext.KeyPrefix}:Setting2"]);
            Assert.Equal("False", config1[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
            Assert.Equal("False", config1[$"FeatureManagement:{testContext.KeyPrefix}Feature2"]);

            Assert.Equal("InitialValue1", config2[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config2[$"{testContext.KeyPrefix}:Setting2"]);
            Assert.Equal("False", config2[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
            Assert.Equal("False", config2[$"FeatureManagement:{testContext.KeyPrefix}Feature2"]);

            // Update all values in the store
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting2", "UpdatedValue2"));

            // Update the feature flags
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":true}",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    secondFeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature2"",""description"":""Second test feature"",""enabled"":true}",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            // Update the sentinel key to trigger refresh
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(testContext.SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act - Refresh both configurations
            await refresher1.RefreshAsync();
            await refresher2.RefreshAsync();

            // Assert - Both configurations should be updated the same way
            // For config1 (Register with refreshAll: true)
            Assert.Equal("UpdatedValue1", config1[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("UpdatedValue2", config1[$"{testContext.KeyPrefix}:Setting2"]);
            Assert.Equal("True", config1[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
            Assert.Equal("True", config1[$"FeatureManagement:{testContext.KeyPrefix}Feature2"]);

            // For config2 (RegisterAll)
            Assert.Equal("UpdatedValue1", config2[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("UpdatedValue2", config2[$"{testContext.KeyPrefix}:Setting2"]);
            Assert.Equal("True", config2[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
            Assert.Equal("True", config2[$"FeatureManagement:{testContext.KeyPrefix}Feature2"]);

            // Test deleting a key and a feature flag
            await _configClient.DeleteConfigurationSettingAsync($"{testContext.KeyPrefix}:Setting2");
            await _configClient.DeleteConfigurationSettingAsync(secondFeatureFlagKey);

            // Update the sentinel key again to trigger refresh
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(testContext.SentinelKey, "UpdatedAgain"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Refresh both configurations again
            await refresher1.RefreshAsync();
            await refresher2.RefreshAsync();

            // Both configurations should have removed the deleted key-value and feature flag
            Assert.Equal("UpdatedValue1", config1[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Null(config1[$"{testContext.KeyPrefix}:Setting2"]);
            Assert.Equal("True", config1[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
            Assert.Null(config1[$"FeatureManagement:{testContext.KeyPrefix}Feature2"]);

            Assert.Equal("UpdatedValue1", config2[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Null(config2[$"{testContext.KeyPrefix}:Setting2"]);
            Assert.Equal("True", config2[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
            Assert.Null(config2[$"FeatureManagement:{testContext.KeyPrefix}Feature2"]);
        }
    }
}
