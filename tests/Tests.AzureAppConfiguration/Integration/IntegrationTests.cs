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
        private const string SentinelKey = TestKeyPrefix + ":Sentinel";
        private const string FeatureFlagKey = ".appconfig.featureflag/" + TestKeyPrefix + "Feature";
        private const string DefaultLocation = "swedensouth";
        private const string SubscriptionJsonPath = "appsettings.Secrets.json";

        /// <summary>
        /// Class to hold test-specific key information
        /// </summary>
        private class TestContext
        {
            public string KeyPrefix { get; set; }
            public string SentinelKey { get; set; }
            public string FeatureFlagKey { get; set; }
        }

        // Keys to create for testing
        private readonly List<ConfigurationSetting> _testSettings = new List<ConfigurationSetting>
        {
            new ConfigurationSetting($"{TestKeyPrefix}:Setting1", "InitialValue1"),
            new ConfigurationSetting($"{TestKeyPrefix}:Setting2", "InitialValue2"),
            new ConfigurationSetting(SentinelKey, "Initial"),
            ConfigurationModelFactory.ConfigurationSetting(
                FeatureFlagKey,
                @"{""id"":""" + TestKeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":false}",
                contentType: FeatureManagementConstants.ContentType)
        };

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

        // Flag indicating whether tests should run
        private bool _skipTests = false;
        private string _skipReason = null;

        /// <summary>
        /// Gets a DefaultAzureCredential for authentication.
        /// </summary>
        private DefaultAzureCredential GetCredential()
        {
            try
            {
                return new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ExcludeSharedTokenCacheCredential = true
                });
            }
            catch (CredentialUnavailableException ex)
            {
                _skipTests = true;
                _skipReason = $"Azure credentials unavailable: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Gets the current subscription ID by reading from the JSON file created by the PowerShell script.
        /// NOTE: The PowerShell script must be run manually before running the tests.
        /// </summary>
        private string GetCurrentSubscriptionId()
        {
            try
            {
                // Read the JSON file created by the script
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Integration", SubscriptionJsonPath);

                if (!File.Exists(jsonPath))
                {
                    _skipTests = true;
                    _skipReason = $"Subscription JSON file not found at {jsonPath}. Run the GetAzureSubscription.ps1 script first.";
                    return null;
                }

                string jsonContent = File.ReadAllText(jsonPath);

                using JsonDocument doc = JsonDocument.Parse(jsonContent);
                JsonElement root = doc.RootElement;

                bool success = root.GetProperty("Success").GetBoolean();

                if (!success)
                {
                    _skipTests = true;
                    _skipReason = root.GetProperty("ErrorMessage").GetString();
                    return null;
                }

                return root.GetProperty("SubscriptionId").GetString();
            }
            catch (FileNotFoundException ex)
            {
                _skipTests = true;
                _skipReason = $"Subscription JSON file not found: {ex.Message}. Run the GetAzureSubscription.ps1 script first.";
                return null;
            }
            catch (JsonException ex)
            {
                _skipTests = true;
                _skipReason = $"Failed to parse subscription JSON: {ex.Message}";
                return null;
            }
            catch (IOException ex)
            {
                _skipTests = true;
                _skipReason = $"IO error while reading subscription data: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Returns the connection string for connecting to the app configuration store.
        /// </summary>
        private string GetConnectionString()
        {
            return _connectionString;
        }

        /// <summary>
        /// Creates a temporary Azure App Configuration store and adds test data.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                var credential = GetCredential();
                if (_skipTests) return;

                // Get the current subscription ID from the JSON file
                _subscriptionId = GetCurrentSubscriptionId();
                if (_skipTests) return;

                // Initialize Azure Resource Manager client
                _armClient = new ArmClient(credential);

                SubscriptionResource subscription = _armClient.GetSubscriptions().Get(_subscriptionId);

                // Create a temporary resource group for this test run
                _testResourceGroupName = $"appconfig-test-{Guid.NewGuid():N}".Substring(0, 20);

                var rgData = new ResourceGroupData(new AzureLocation(DefaultLocation));

                try
                {
                    var rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, _testResourceGroupName, rgData);
                    _resourceGroup = rgLro.Value;
                }
                catch (RequestFailedException ex)
                {
                    _skipTests = true;
                    _skipReason = $"Failed to create resource group: {ex.Message}";
                    return;
                }

                // Create unique store name for this test run
                _testStoreName = $"integration-{Guid.NewGuid():N}".Substring(0, 20);

                // Create the App Configuration store
                var storeData = new AppConfigurationStoreData(new AzureLocation(DefaultLocation), new AppConfigurationSku("free"));

                try
                {
                    var createOperation = await _resourceGroup.GetAppConfigurationStores().CreateOrUpdateAsync(
                        WaitUntil.Completed,
                        _testStoreName,
                        storeData);

                    _appConfigStore = createOperation.Value;
                    _appConfigEndpoint = new Uri(_appConfigStore.Data.Endpoint);
                }
                catch (RequestFailedException ex)
                {
                    _skipTests = true;
                    _skipReason = $"Failed to create App Configuration store: {ex.Message}";
                    await CleanupResourceGroup();
                    return;
                }

                // Get the connection string for the store
                try
                {
                    var accessKeys = _appConfigStore.GetKeysAsync();
                    var primaryKey = await accessKeys.FirstOrDefaultAsync();

                    if (primaryKey == null)
                    {
                        throw new InvalidOperationException("Failed to retrieve access keys from App Configuration store.");
                    }

                    _connectionString = primaryKey.ConnectionString;

                    // Initialize the configuration client with the connection string
                    _configClient = new ConfigurationClient(_connectionString);
                }
                catch (RequestFailedException ex)
                {
                    _skipTests = true;
                    _skipReason = $"Failed to get access keys: {ex.Message}";
                    await CleanupResourceGroup();
                    return;
                }

                // Add test settings to the store
                try
                {
                    foreach (var setting in _testSettings)
                    {
                        await _configClient.SetConfigurationSettingAsync(setting);
                    }
                }
                catch (RequestFailedException ex)
                {
                    _skipTests = true;
                    _skipReason = $"Failed to set configuration settings: {ex.Message}";
                    await CleanupResourceGroup();
                }
            }
            catch (CredentialUnavailableException ex)
            {
                _skipTests = true;
                _skipReason = $"Azure credentials unavailable: {ex.Message}";
                await CleanupResourceGroup();
            }
            catch (InvalidOperationException ex)
            {
                _skipTests = true;
                _skipReason = $"Failed to initialize integration tests: {ex.Message}";
                await CleanupResourceGroup();
            }
            catch (RequestFailedException ex)
            {
                _skipTests = true;
                _skipReason = $"Azure request failed: {ex.Message}";
                await CleanupResourceGroup();
            }
            catch (TaskCanceledException ex)
            {
                _skipTests = true;
                _skipReason = $"Operation timed out: {ex.Message}";
                await CleanupResourceGroup();
            }
        }

        /// <summary>
        /// Helper method to clean up the resource group if initialization fails
        /// </summary>
        private async Task CleanupResourceGroup()
        {
            if (_resourceGroup != null)
            {
                try
                {
                    await _resourceGroup.DeleteAsync(WaitUntil.Completed);
                    _resourceGroup = null;
                }
                catch (RequestFailedException)
                {
                    // Ignore exceptions during cleanup
                }
                catch (TaskCanceledException)
                {
                    // Ignore timeout exceptions during cleanup
                }
            }
        }

        /// <summary>
        /// Cleans up the temporary App Configuration store after tests are complete.
        /// </summary>
        public async Task DisposeAsync()
        {
            // Don't attempt cleanup if we don't have a resource group to delete
            if (_resourceGroup == null)
            {
                return;
            }

            try
            {
                await _resourceGroup.DeleteAsync(WaitUntil.Completed);
                _resourceGroup = null;
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Test cleanup failed: {ex.Message}. You may need to manually delete the resources: Store={_testStoreName}, ResourceGroup={_testResourceGroupName}");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Test cleanup failed: {ex.Message}. You may need to manually delete the resources: Store={_testStoreName}, ResourceGroup={_testResourceGroupName}");
            }
            catch (TaskCanceledException ex)
            {
                Console.WriteLine($"Test cleanup timed out: {ex.Message}. You may need to manually delete the resources: Store={_testStoreName}, ResourceGroup={_testResourceGroupName}");
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
                    contentType: FeatureManagementConstants.ContentType)
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
        public void LoadConfiguration_RetrievesValuesFromAppConfiguration()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange & Act
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{TestKeyPrefix}:*");
                })
                .Build();

            // Assert
            Assert.Equal("InitialValue1", config[$"{TestKeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{TestKeyPrefix}:Setting2"]);
        }

        [Fact]
        public async Task RefreshAsync_UpdatesConfiguration_WhenSentinelKeyChanged()
        {
            Skip.If(_skipTests, _skipReason);

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
        public async Task RefreshAsync_RefreshesOnlySelectedKeys_WhenUsingKeyFilter()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("RefreshesSelectedKeys");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");

                    // Only refresh Setting1 when sentinel changes
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey, $"{testContext.KeyPrefix}:Setting1", refreshAll: false)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]);

            // Update values in the store
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting2", "UpdatedValue2"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(testContext.SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            await refresher.RefreshAsync();

            // Assert
            Assert.Equal("UpdatedValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]); // This value shouldn't change
        }

        [Fact]
        public async Task RefreshAsync_RefreshesFeatureFlags_WhenConfigured()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("RefreshesFeatureFlags");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");
                    options.UseFeatureFlags();

                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial feature flag state
            Assert.Equal("False", config[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);

            // Update feature flag in the store
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":true}",
                    contentType: FeatureManagementConstants.ContentType));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(testContext.SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            await refresher.RefreshAsync();

            // Assert
            Assert.Equal("True", config[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
        }

        [Fact]
        public async Task RegisterAll_RefreshesAllKeys_WhenSentinelChanged()
        {
            Skip.If(_skipTests, _skipReason);

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
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(testContext.SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            await refresher.RefreshAsync();

            // Assert
            Assert.Equal("UpdatedValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("UpdatedValue2", config[$"{testContext.KeyPrefix}:Setting2"]);
        }

        [Fact]
        public async Task RefreshAsync_ReturnsFalse_WhenSentinelKeyUnchanged()
        {
            Skip.If(_skipTests, _skipReason);

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
    }
}
