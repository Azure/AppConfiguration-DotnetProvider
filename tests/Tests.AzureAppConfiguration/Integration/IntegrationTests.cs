using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppConfiguration;
using Azure.ResourceManager.KeyVault;
using Azure.ResourceManager.Resources;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
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
    /// Uses an existing App Configuration store and Key Vault for testing.
    /// Requires Azure credentials with appropriate permissions.
    /// NOTE: Before running these tests, execute the GetAzureSubscription.ps1 script to create appsettings.Secrets.json.
    /// </summary>
    [Trait("Category", "Integration")]
    [CollectionDefinition(nameof(IntegrationTests), DisableParallelization = true)]
    public class IntegrationTests : IAsyncLifetime
    {
        // Test constants
        private const string TestKeyPrefix = "IntegrationTest";
        private const string SubscriptionJsonPath = "appsettings.Secrets.json";
        private static readonly TimeSpan StaleResourceThreshold = TimeSpan.FromHours(3);
        private const string KeyVaultReferenceLabel = "KeyVaultRef";

        // Fixed resource names - already existing
        private const string AppConfigStoreName = "appconfig-dotnetprovider-integrationtest";
        private const string KeyVaultName = "keyvault-dotnetprovider";
        private const string ResourceGroupName = "dotnetprovider-integrationtest";

        /// <summary>
        /// Class to hold test-specific key information
        /// </summary>
        private class TestContext
        {
            public string KeyPrefix { get; set; }
            public string SentinelKey { get; set; }
            public string FeatureFlagKey { get; set; }
            public string KeyVaultReferenceKey { get; set; }
            public string SecretName { get; set; }
            public string SecretValue { get; set; }
        }

        // Client for direct manipulation of the store
        private ConfigurationClient _configClient;

        // Client for Key Vault operations
        private SecretClient _secretClient;

        // Connection string for the store
        private string _connectionString;

        // Endpoints for the resources
        private Uri _appConfigEndpoint;
        private Uri _keyVaultEndpoint;

        // Track resources created by tests for cleanup
        private readonly HashSet<string> _createdConfigKeys = new HashSet<string>();
        private readonly HashSet<string> _createdSecretNames = new HashSet<string>();
        private readonly HashSet<string> _createdSnapshotNames = new HashSet<string>();

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
            string subscriptionIdFromEnv = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");

            if (!string.IsNullOrEmpty(subscriptionIdFromEnv))
            {
                return subscriptionIdFromEnv;
            }

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
        /// Creates a unique prefix for test keys to ensure test isolation
        /// </summary>
        private string GetUniqueKeyPrefix(string testName)
        {
            // Use a combination of the test prefix and test method name to ensure uniqueness
            return $"{TestKeyPrefix}-{testName}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        /// <summary>
        /// Creates a snapshot with the given name containing the test context's settings
        /// </summary>
        private async Task<string> CreateSnapshot(string snapshotName, TestContext testContext)
        {
            // Create a snapshot with the test keys
            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter($"{testContext.KeyPrefix}:*")
            };

            ConfigurationSnapshot snapshot = new ConfigurationSnapshot(settingsToInclude);

            snapshot.SnapshotComposition = SnapshotComposition.Key;

            try
            {
                // Create the snapshot
                CreateSnapshotOperation operation = await _configClient.CreateSnapshotAsync(WaitUntil.Completed, snapshotName, snapshot);

                // Track created snapshot for cleanup
                _createdSnapshotNames.Add(snapshotName);

                return operation.Value.Name;
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Error creating snapshot: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Initialize clients to connect to the existing App Configuration store and Key Vault.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                var credential = GetCredential();
                string subscriptionId = GetCurrentSubscriptionId();

                // Initialize Azure Resource Manager client
                var armClient = new ArmClient(credential);
                SubscriptionResource subscription = armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}"));

                ResourceGroupResource resourceGroup = await subscription.GetResourceGroups().GetAsync(ResourceGroupName);

                AppConfigurationStoreResource appConfigStore = null;
                KeyVaultResource keyVault = null;

                try
                {
                    // Get App Configuration store directly using the resource group and store name
                    appConfigStore = await resourceGroup.GetAppConfigurationStores().GetAsync(AppConfigStoreName);
                }
                catch (RequestFailedException ex)
                {
                    throw new InvalidOperationException($"App Configuration store '{AppConfigStoreName}' not found in resource group '{ResourceGroupName}'. Please create it before running tests.", ex);
                }

                _appConfigEndpoint = new Uri(appConfigStore.Data.Endpoint);

                // Get connection string from the store
                var accessKeys = appConfigStore.GetKeysAsync();
                var primaryKey = await accessKeys.FirstOrDefaultAsync();

                if (primaryKey == null)
                {
                    throw new InvalidOperationException("Failed to retrieve access keys from App Configuration store.");
                }

                _connectionString = primaryKey.ConnectionString;

                // Initialize the configuration client with the connection string
                _configClient = new ConfigurationClient(_connectionString);

                // Find and initialize Key Vault - look in the same resource group
                keyVault = await resourceGroup.GetKeyVaults().GetAsync(KeyVaultName);

                if (keyVault == null)
                {
                    throw new InvalidOperationException($"Key Vault '{KeyVaultName}' not found in subscription {subscriptionId}. Please create it before running tests.");
                }

                _keyVaultEndpoint = keyVault.Data.Properties.VaultUri;

                // Create a Secret Client for the vault
                _secretClient = new SecretClient(_keyVaultEndpoint, credential);

                Console.WriteLine($"Successfully connected to App Configuration store '{AppConfigStoreName}' and Key Vault '{KeyVaultName}'");

                // Clean up stale resources on startup
                await CleanupStaleResources();
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Azure request failed: {ex.Message}. Status code: {ex.Status}");
                throw;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"Invalid argument: {ex.Message}");
                throw;
            }
            catch (InvalidOperationException ex)
            {
                // This is already a specific exception, so just rethrow
                Console.WriteLine($"Invalid operation: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Cleans up stale resources that are older than the threshold
        /// </summary>
        private async Task CleanupStaleResources()
        {
            Console.WriteLine($"Checking for stale resources older than {StaleResourceThreshold}...");
            var cutoffTime = DateTimeOffset.UtcNow.Subtract(StaleResourceThreshold);
            var cleanupTasks = new List<Task>();

            try
            {
                // Clean up stale configuration settings
                int staleConfigCount = 0;
                var configSettingsToCleanup = new List<ConfigurationSetting>();

                // Get all test key-values
                var configSettings = _configClient.GetConfigurationSettingsAsync(new SettingSelector
                {
                    KeyFilter = TestKeyPrefix + "*"
                });

                await foreach (var setting in configSettings)
                {
                    // Check if the setting is older than the threshold
                    if (setting.LastModified < cutoffTime)
                    {
                        configSettingsToCleanup.Add(setting);
                        staleConfigCount++;
                    }
                }

                // Clean up stale feature flags
                var featureFlagSettings = _configClient.GetConfigurationSettingsAsync(new SettingSelector
                {
                    KeyFilter = ".appconfig.featureflag/" + TestKeyPrefix + "*"
                });

                await foreach (var setting in featureFlagSettings)
                {
                    if (setting.LastModified < cutoffTime)
                    {
                        configSettingsToCleanup.Add(setting);
                        staleConfigCount++;
                    }
                }

                // Delete stale configuration settings
                foreach (var setting in configSettingsToCleanup)
                {
                    cleanupTasks.Add(_configClient.DeleteConfigurationSettingAsync(setting.Key, setting.Label));
                }

                // Clean up stale snapshots
                int staleSnapshotCount = 0;
                var snapshots = _configClient.GetSnapshotsAsync(new SnapshotSelector());
                await foreach (var snapshot in snapshots)
                {
                    if (snapshot.Name.StartsWith("snapshot-" + TestKeyPrefix) && snapshot.CreatedOn < cutoffTime)
                    {
                        cleanupTasks.Add(_configClient.ArchiveSnapshotAsync(snapshot.Name));
                        staleSnapshotCount++;
                    }
                }

                // Clean up stale Key Vault secrets
                int staleSecretCount = 0;
                if (_secretClient != null)
                {
                    var secrets = _secretClient.GetPropertiesOfSecretsAsync();
                    await foreach (var secretProperties in secrets)
                    {
                        if (secretProperties.Name.StartsWith(TestKeyPrefix) && secretProperties.CreatedOn.HasValue && secretProperties.CreatedOn.Value < cutoffTime)
                        {
                            cleanupTasks.Add(_secretClient.StartDeleteSecretAsync(secretProperties.Name));
                            staleSecretCount++;
                        }
                    }
                }

                // Wait for all cleanup tasks to complete
                await Task.WhenAll(cleanupTasks);
                Console.WriteLine($"Cleaned up {staleConfigCount} stale configuration settings, {staleSnapshotCount} snapshots, and {staleSecretCount} secrets");
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Error during stale resource cleanup: {ex.Message}");
                // Continue execution even if cleanup fails
            }
        }

        /// <summary>
        /// Clean up test artifacts but don't delete the actual resources
        /// </summary>
        public async Task DisposeAsync()
        {
            var cleanupTasks = new List<Task>();

            try
            {
                // Clean up all configuration settings created by tests
                foreach (var key in _createdConfigKeys)
                {
                    try
                    {
                        cleanupTasks.Add(_configClient.DeleteConfigurationSettingAsync(key));
                    }
                    catch (RequestFailedException ex)
                    {
                        Console.WriteLine($"Failed to delete configuration setting {key}: {ex.Message}");
                    }
                }

                // Clean up all snapshots created by tests
                foreach (var snapshotName in _createdSnapshotNames)
                {
                    try
                    {
                        cleanupTasks.Add(_configClient.ArchiveSnapshotAsync(snapshotName));
                    }
                    catch (RequestFailedException ex)
                    {
                        Console.WriteLine($"Failed to delete snapshot {snapshotName}: {ex.Message}");
                    }
                }

                // Clean up test-specific secrets in Key Vault
                if (_secretClient != null)
                {
                    foreach (var secretName in _createdSecretNames)
                    {
                        try
                        {
                            cleanupTasks.Add(_secretClient.StartDeleteSecretAsync(secretName));
                        }
                        catch (RequestFailedException ex)
                        {
                            Console.WriteLine($"Failed to delete secret {secretName}: {ex.Message}");
                        }
                    }
                }

                // Wait for all cleanup tasks to complete
                await Task.WhenAll(cleanupTasks);

                Console.WriteLine($"Cleaned up {_createdConfigKeys.Count} configuration settings, {_createdSnapshotNames.Count} snapshots, and {_createdSecretNames.Count} secrets");
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Error during resource cleanup: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Operation error during test cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Setup test-specific keys and settings
        /// </summary>
        private async Task<TestContext> SetupTestKeys(string testName)
        {
            string keyPrefix = GetUniqueKeyPrefix(testName);
            string sentinelKey = $"{keyPrefix}:Sentinel";
            string featureFlagKey = $".appconfig.featureflag/{keyPrefix}Feature";
            string secretName = $"{keyPrefix}-secret";
            string secretValue = "SecretValue";
            string keyVaultReferenceKey = $"{keyPrefix}:KeyVaultRef";

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
                // Track the created key for cleanup
                _createdConfigKeys.Add(setting.Key);
            }

            // If Key Vault is available, add a test secret and reference
            if (_secretClient != null)
            {
                try
                {
                    // Create a secret in Key Vault
                    await _secretClient.SetSecretAsync(secretName, secretValue);
                    // Track the created secret for cleanup
                    _createdSecretNames.Add(secretName);

                    // Create a Key Vault reference in App Configuration
                    string keyVaultUri = $"{_keyVaultEndpoint}secrets/{secretName}";
                    string keyVaultRefValue = @$"{{""uri"":""{keyVaultUri}""}}";

                    var keyVaultRefSetting = ConfigurationModelFactory.ConfigurationSetting(
                        keyVaultReferenceKey,
                        keyVaultRefValue,
                        label: KeyVaultReferenceLabel,
                        contentType: KeyVaultConstants.ContentType);

                    await _configClient.SetConfigurationSettingAsync(keyVaultRefSetting);
                    // Track the created key reference for cleanup
                    _createdConfigKeys.Add(keyVaultReferenceKey);
                }
                catch (RequestFailedException ex)
                {
                    Console.WriteLine($"Error setting up Key Vault secret: {ex.Message}");
                    // Continue without Key Vault reference if it fails
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"Invalid Key Vault operation: {ex.Message}");
                    // Continue without Key Vault reference if it fails
                }
            }

            return new TestContext
            {
                KeyPrefix = keyPrefix,
                SentinelKey = sentinelKey,
                FeatureFlagKey = featureFlagKey,
                KeyVaultReferenceKey = keyVaultReferenceKey,
                SecretName = secretName,
                SecretValue = secretValue
            };
        }

        // Helper method to track additional configuration keys created during tests
        private void TrackConfigurationKey(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                _createdConfigKeys.Add(key);
            }
        }

        // Helper method to track additional secrets created during tests
        private void TrackKeyVaultSecret(string secretName)
        {
            if (!string.IsNullOrEmpty(secretName))
            {
                _createdSecretNames.Add(secretName);
            }
        }

        // Helper method to track additional snapshots created during tests
        private void TrackSnapshot(string snapshotName)
        {
            if (!string.IsNullOrEmpty(snapshotName))
            {
                _createdSnapshotNames.Add(snapshotName);
            }
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
                        refresh.Register(testContext.SentinelKey, true)
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
                        refresh.Register(testContext.SentinelKey, true)
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
                        refresh.Register(testContext.SentinelKey, true)
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
                        refresh.Register(testContext.SentinelKey, true)
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

        [Fact]
        public async Task HandlesFailoverOnStartup()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("FailoverStartup");
            IConfigurationRefresher refresher = null;

            string connectionString = GetConnectionString();

            // Create a connection string that will fail
            string primaryConnectionString = ConnectionStringUtils.Build(
                TestHelpers.PrimaryConfigStoreEndpoint,
                ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.IdSection),
                ConnectionStringUtils.Parse(connectionString, ConnectionStringUtils.SecretSection));
            string secondaryConnectionString = connectionString;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(new List<string> { primaryConnectionString, secondaryConnectionString });
                    options.Select($"{testContext.KeyPrefix}:*");

                    // Configure refresh
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
        }

        /// <summary>
        /// Test verifies that a snapshot can be created and loaded correctly
        /// </summary>
        [Fact]
        public async Task LoadSnapshot_RetrievesValuesFromSnapshot()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("SnapshotTest");
            string snapshotName = $"snapshot-{testContext.KeyPrefix}";

            // Create a snapshot with the test keys
            await CreateSnapshot(snapshotName, testContext);

            // Update values after snapshot is taken to verify snapshot has original values
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting1", "UpdatedAfterSnapshot"));

            // Act - Load configuration from snapshot
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.SelectSnapshot(snapshotName);
                })
                .Build();

            // Assert - Should have original values from snapshot, not updated values
            Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]);

            // Cleanup - Delete the snapshot
            await _configClient.ArchiveSnapshotAsync(snapshotName);
        }

        /// <summary>
        /// Test verifies error handling when a snapshot doesn't exist
        /// </summary>
        [Fact]
        public async Task LoadSnapshot_ThrowsException_WhenSnapshotDoesNotExist()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("NonExistentSnapshotTest");
            string nonExistentSnapshotName = $"snapshot-does-not-exist-{Guid.NewGuid()}";

            // Act & Assert - Loading a non-existent snapshot should throw
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            {
                return Task.FromResult(new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(GetConnectionString());
                        options.SelectSnapshot(nonExistentSnapshotName);
                    })
                    .Build());
            });

            // Verify the exception message contains snapshot name
            Assert.Contains(nonExistentSnapshotName, exception.Message);
        }

        /// <summary>
        /// Test verifies that multiple snapshots can be loaded in the same configuration
        /// </summary>
        [Fact]
        public async Task LoadMultipleSnapshots_MergesConfigurationCorrectly()
        {
            // Arrange - Setup test-specific keys for two separate snapshots
            var testContext1 = await SetupTestKeys("SnapshotMergeTest1");
            var testContext2 = await SetupTestKeys("SnapshotMergeTest2");

            // Create specific values for second snapshot
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{testContext2.KeyPrefix}:UniqueKey", "UniqueValue"));

            string snapshotName1 = $"snapshot-{testContext1.KeyPrefix}";
            string snapshotName2 = $"snapshot-{testContext2.KeyPrefix}";

            // Create snapshots
            await CreateSnapshot(snapshotName1, testContext1);
            await CreateSnapshot(snapshotName2, testContext2);

            try
            {
                // Act - Load configuration from both snapshots
                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(GetConnectionString());
                        options.SelectSnapshot(snapshotName1);
                        options.SelectSnapshot(snapshotName2);
                    })
                    .Build();

                // Assert - Should have values from both snapshots
                Assert.Equal("InitialValue1", config[$"{testContext1.KeyPrefix}:Setting1"]);
                Assert.Equal("InitialValue1", config[$"{testContext2.KeyPrefix}:Setting1"]);
                Assert.Equal("UniqueValue", config[$"{testContext2.KeyPrefix}:UniqueKey"]);
            }
            finally
            {
                // Cleanup - Delete the snapshots
                await _configClient.ArchiveSnapshotAsync(snapshotName1);
                await _configClient.ArchiveSnapshotAsync(snapshotName2);
            }
        }

        /// <summary>
        /// Test verifies that different snapshot composition types are handled correctly
        /// </summary>
        [Fact]
        public async Task SnapshotCompositionTypes_AreHandledCorrectly()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("SnapshotCompositionTest");
            string keyOnlySnapshotName = $"snapshot-key-{testContext.KeyPrefix}";
            string invalidCompositionSnapshotName = $"snapshot-invalid-{testContext.KeyPrefix}";

            // Create a snapshot with the test keys
            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter($"{testContext.KeyPrefix}:*")
            };

            ConfigurationSnapshot keyOnlySnapshot = new ConfigurationSnapshot(settingsToInclude);

            keyOnlySnapshot.SnapshotComposition = SnapshotComposition.Key;

            // Create the snapshot
            await _configClient.CreateSnapshotAsync(WaitUntil.Completed, keyOnlySnapshotName, keyOnlySnapshot);

            ConfigurationSnapshot invalidSnapshot = new ConfigurationSnapshot(settingsToInclude);

            invalidSnapshot.SnapshotComposition = SnapshotComposition.KeyLabel;

            // Create the snapshot
            await _configClient.CreateSnapshotAsync(WaitUntil.Completed, invalidCompositionSnapshotName, invalidSnapshot);

            try
            {
                // Act & Assert - Loading a key-only snapshot should work
                var config1 = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(GetConnectionString());
                        options.SelectSnapshot(keyOnlySnapshotName);
                    })
                    .Build();

                Assert.Equal("InitialValue1", config1[$"{testContext.KeyPrefix}:Setting1"]);

                // Act & Assert - Loading a snapshot with invalid composition should throw
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                {
                    return Task.FromResult(new ConfigurationBuilder()
                        .AddAzureAppConfiguration(options =>
                        {
                            options.Connect(GetConnectionString());
                            options.SelectSnapshot(invalidCompositionSnapshotName);
                        })
                        .Build());
                });

                // Verify the exception message mentions composition type
                Assert.Contains("SnapshotComposition", exception.Message);
                Assert.Contains("key", exception.Message);
                Assert.Contains("label", exception.Message);
            }
            finally
            {
                // Cleanup - Delete the snapshots
                await _configClient.ArchiveSnapshotAsync(keyOnlySnapshotName);
                await _configClient.ArchiveSnapshotAsync(invalidCompositionSnapshotName);
            }
        }

        /// <summary>
        /// Test verifies that snapshots work with feature flags
        /// </summary>
        [Fact]
        public async Task SnapshotWithFeatureFlags_LoadsConfigurationCorrectly()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("SnapshotFeatureFlagTest");
            string snapshotName = $"snapshot-ff-{testContext.KeyPrefix}";

            // Update the feature flag to be enabled before creating the snapshot
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":true}",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            // Create a snapshot with the test keys
            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter($"{testContext.KeyPrefix}:*"),
                new ConfigurationSettingsFilter($".appconfig.featureflag/{testContext.KeyPrefix}*")
            };

            ConfigurationSnapshot snapshot = new ConfigurationSnapshot(settingsToInclude);

            snapshot.SnapshotComposition = SnapshotComposition.Key;

            // Create the snapshot
            await _configClient.CreateSnapshotAsync(WaitUntil.Completed, snapshotName, snapshot);

            // Update feature flag to disabled after creating snapshot
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":false}",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            try
            {
                // Act - Load configuration from snapshot with feature flags
                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(GetConnectionString());
                        options.UseFeatureFlags();
                        options.SelectSnapshot(snapshotName);
                    })
                    .Build();

                // Assert - Should have feature flag enabled state from snapshot
                Assert.Equal("True", config[$"FeatureManagement:{testContext.KeyPrefix}Feature"]);
            }
            finally
            {
                // Cleanup - Delete the snapshot
                await _configClient.ArchiveSnapshotAsync(snapshotName);
            }
        }

        /// <summary>
        /// Test verifies call ordering of snapshots, select, and feature flags
        /// </summary>
        [Fact]
        public async Task CallOrdering_SnapshotsWithSelectAndFeatureFlags()
        {
            // Arrange - Setup test-specific keys for multiple snapshots
            var mainContext = await SetupTestKeys("SnapshotOrdering");
            var secondContext = await SetupTestKeys("SnapshotOrdering2");
            var thirdContext = await SetupTestKeys("SnapshotOrdering3");

            // Create specific values for each snapshot
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{mainContext.KeyPrefix}:UniqueMain", "MainValue"));

            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{secondContext.KeyPrefix}:UniqueSecond", "SecondValue"));

            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{thirdContext.KeyPrefix}:UniqueThird", "ThirdValue"));

            // Create additional feature flags
            string secondFeatureFlagKey = $".appconfig.featureflag/{mainContext.KeyPrefix}Feature2";
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    secondFeatureFlagKey,
                    @"{""id"":""" + mainContext.KeyPrefix + @"Feature2"",""description"":""Second test feature"",""enabled"":true}",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            string thirdFeatureFlagKey = $".appconfig.featureflag/{secondContext.KeyPrefix}Feature";
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    thirdFeatureFlagKey,
                    @"{""id"":""" + secondContext.KeyPrefix + @"Feature"",""description"":""Third test feature"",""enabled"":true}",
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            // Create snapshots
            string snapshot1 = $"snapshot-{mainContext.KeyPrefix}-1";
            string snapshot2 = $"snapshot-{secondContext.KeyPrefix}-2";
            string snapshot3 = $"snapshot-{thirdContext.KeyPrefix}-3";

            await CreateSnapshot(snapshot1, mainContext);
            await CreateSnapshot(snapshot2, secondContext);
            await CreateSnapshot(snapshot3, thirdContext);

            try
            {
                // Test different orderings of SelectSnapshot, Select and UseFeatureFlags

                // Order 1: SelectSnapshot -> Select -> UseFeatureFlags
                var config1 = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(GetConnectionString());
                        options.SelectSnapshot(snapshot1);
                        options.Select($"{mainContext.KeyPrefix}:*");
                        options.UseFeatureFlags(ff =>
                        {
                            ff.Select($"{mainContext.KeyPrefix}Feature*");
                        });
                    })
                    .Build();

                // Order 2: Select -> SelectSnapshot -> UseFeatureFlags
                var config2 = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(GetConnectionString());
                        options.Select($"{secondContext.KeyPrefix}:*");
                        options.SelectSnapshot(snapshot2);
                        options.UseFeatureFlags(ff =>
                        {
                            ff.Select($"{secondContext.KeyPrefix}Feature*");
                        });
                    })
                    .Build();

                // Order 3: UseFeatureFlags -> SelectSnapshot -> Select
                var config3 = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(GetConnectionString());
                        options.UseFeatureFlags();
                        options.SelectSnapshot(snapshot3);
                        options.Select($"{thirdContext.KeyPrefix}:*");
                    })
                    .Build();

                // Order 4: Multiple snapshots with interleaved operations
                var config4 = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(GetConnectionString());
                        options.SelectSnapshot(snapshot1);
                        options.UseFeatureFlags(ff =>
                        {
                            ff.Select($"{mainContext.KeyPrefix}Feature*");
                        });
                        options.SelectSnapshot(snapshot2);
                        options.Select($"{secondContext.KeyPrefix}:*");
                        options.UseFeatureFlags(ff =>
                        {
                            ff.Select($"{secondContext.KeyPrefix}Feature*");
                        });
                        options.SelectSnapshot(snapshot3);
                    })
                    .Build();

                // Verify config1: Should have values from snapshot1 and feature flags from mainContext
                Assert.Equal("InitialValue1", config1[$"{mainContext.KeyPrefix}:Setting1"]);
                Assert.Equal("MainValue", config1[$"{mainContext.KeyPrefix}:UniqueMain"]);
                Assert.Equal("False", config1[$"FeatureManagement:{mainContext.KeyPrefix}Feature"]);
                Assert.Equal("True", config1[$"FeatureManagement:{mainContext.KeyPrefix}Feature2"]);

                // Verify config2: Should have values from snapshot2 and feature flags from secondContext
                Assert.Equal("InitialValue1", config2[$"{secondContext.KeyPrefix}:Setting1"]);
                Assert.Equal("SecondValue", config2[$"{secondContext.KeyPrefix}:UniqueSecond"]);
                Assert.Equal("True", config2[$"FeatureManagement:{secondContext.KeyPrefix}Feature"]);

                // Verify config3: Should have values from snapshot3 and all feature flags
                Assert.Equal("InitialValue1", config3[$"{thirdContext.KeyPrefix}:Setting1"]);
                Assert.Equal("ThirdValue", config3[$"{thirdContext.KeyPrefix}:UniqueThird"]);
                Assert.Equal("False", config3[$"FeatureManagement:{mainContext.KeyPrefix}Feature"]);
                Assert.Equal("True", config3[$"FeatureManagement:{secondContext.KeyPrefix}Feature"]);

                // Verify config4: Should have values from all three snapshots
                Assert.Equal("InitialValue1", config4[$"{mainContext.KeyPrefix}:Setting1"]);
                Assert.Equal("MainValue", config4[$"{mainContext.KeyPrefix}:UniqueMain"]);
                Assert.Equal("InitialValue1", config4[$"{secondContext.KeyPrefix}:Setting1"]);
                Assert.Equal("SecondValue", config4[$"{secondContext.KeyPrefix}:UniqueSecond"]);
                Assert.Equal("InitialValue1", config4[$"{thirdContext.KeyPrefix}:Setting1"]);
                Assert.Equal("ThirdValue", config4[$"{thirdContext.KeyPrefix}:UniqueThird"]);
                Assert.Equal("False", config4[$"FeatureManagement:{mainContext.KeyPrefix}Feature"]);
                Assert.Equal("True", config4[$"FeatureManagement:{mainContext.KeyPrefix}Feature2"]);
                Assert.Equal("True", config4[$"FeatureManagement:{secondContext.KeyPrefix}Feature"]);
            }
            finally
            {
                // Cleanup - Delete the snapshots
                await _configClient.ArchiveSnapshotAsync(snapshot1);
                await _configClient.ArchiveSnapshotAsync(snapshot2);
                await _configClient.ArchiveSnapshotAsync(snapshot3);
            }
        }

        /// <summary>
        /// Test verifies Key Vault references can be resolved
        /// </summary>
        [Fact]
        public async Task KeyVaultReferences_ResolveCorrectly()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("KeyVaultReference");

            // Act - Create configuration with Key Vault support
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*", KeyVaultReferenceLabel);
                    options.ConfigureKeyVault(kv => kv.SetCredential(GetCredential()));
                })
                .Build();

            // Assert - Key Vault reference should be resolved to the secret value
            Assert.Equal("SecretValue", config[testContext.KeyVaultReferenceKey]);
        }

        /// <summary>
        /// Helper class to monitor Key Vault requests
        /// </summary>
        private class HttpPipelineTransportWithRequestCount : HttpPipelineTransport
        {
            private readonly HttpClientTransport _innerTransport = new HttpClientTransport();
            private readonly Action _onRequest;

            public HttpPipelineTransportWithRequestCount(Action onRequest)
            {
                _onRequest = onRequest;
            }

            public override Request CreateRequest()
            {
                return _innerTransport.CreateRequest();
            }

            public override void Process(HttpMessage message)
            {
                _onRequest();
                _innerTransport.Process(message);
            }

            public override ValueTask ProcessAsync(HttpMessage message)
            {
                _onRequest();
                return _innerTransport.ProcessAsync(message);
            }
        }

        /// <summary>
        /// Tests that Key Vault secrets are properly cached to avoid unnecessary requests.
        /// </summary>
        [Fact]
        public async Task KeyVaultReference_UsesCache_DoesNotCallKeyVaultAgain()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("KeyVaultCacheTest");

            // Create a monitoring client to track calls to Key Vault
            int requestCount = 0;
            var testSecretClient = new SecretClient(
                _keyVaultEndpoint,
                GetCredential(),
                new SecretClientOptions
                {
                    Transport = new HttpPipelineTransportWithRequestCount(() => requestCount++)
                });

            // Act
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*", KeyVaultReferenceLabel);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(testSecretClient);
                    });
                })
                .Build();

            // First access should resolve from Key Vault
            var firstValue = config[testContext.KeyVaultReferenceKey];
            int firstRequestCount = requestCount;

            // Second access should use the cache
            var secondValue = config[testContext.KeyVaultReferenceKey];
            int secondRequestCount = requestCount;

            // Assert
            Assert.Equal(testContext.SecretValue, firstValue);
            Assert.Equal(testContext.SecretValue, secondValue);
            Assert.Equal(1, firstRequestCount);  // Should make exactly one request
            Assert.Equal(firstRequestCount, secondRequestCount); // No additional requests for the second access
        }

        /// <summary>
        /// Tests that different Key Vault references can have different refresh intervals.
        /// </summary>
        [Fact]
        public async Task KeyVaultReference_DifferentRefreshIntervals()
        {
            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("KeyVaultDifferentIntervals");
            IConfigurationRefresher refresher = null;

            // Create a secret in Key Vault with short refresh interval
            string secretName1 = $"test-secret1-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            string secretValue1 = $"SecretValue1-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            await _secretClient.SetSecretAsync(secretName1, secretValue1);

            // Create another secret in Key Vault with long refresh interval
            string secretName2 = $"test-secret2-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            string secretValue2 = $"SecretValue2-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            await _secretClient.SetSecretAsync(secretName2, secretValue2);

            // Create Key Vault references in App Configuration
            string keyVaultUri = _keyVaultEndpoint.ToString().TrimEnd('/');
            string kvRefKey1 = $"{testContext.KeyPrefix}:KeyVaultRef1";
            string kvRefKey2 = $"{testContext.KeyPrefix}:KeyVaultRef2";

            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    kvRefKey1,
                    $@"{{""uri"":""{keyVaultUri}/secrets/{secretName1}""}}",
                    label: KeyVaultReferenceLabel,
                    contentType: KeyVaultConstants.ContentType + "; charset=utf-8"));

            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    kvRefKey2,
                    $@"{{""uri"":""{keyVaultUri}/secrets/{secretName2}""}}",
                    label: KeyVaultReferenceLabel,
                    contentType: KeyVaultConstants.ContentType + "; charset=utf-8"));

            // Act - Create configuration with different refresh intervals
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*", KeyVaultReferenceLabel);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetCredential(GetCredential());
                        // Set different refresh intervals for each secret
                        kv.SetSecretRefreshInterval(kvRefKey1, TimeSpan.FromSeconds(60)); // Short interval
                        kv.SetSecretRefreshInterval(kvRefKey2, TimeSpan.FromDays(1));    // Long interval
                    });
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey, refreshAll: true)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal(secretValue1, config[kvRefKey1]);
            Assert.Equal(secretValue2, config[kvRefKey2]);

            // Update both secrets in Key Vault
            string updatedValue1 = $"UpdatedValue1-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            string updatedValue2 = $"UpdatedValue2-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            await _secretClient.SetSecretAsync(secretName1, updatedValue1);
            await _secretClient.SetSecretAsync(secretName2, updatedValue2);

            // Wait for the short interval cache to expire
            await Task.Delay(TimeSpan.FromSeconds(61));

            // Refresh the configuration
            await refresher.RefreshAsync();

            // Assert - Only the first secret should be refreshed due to having a short interval
            Assert.Equal(updatedValue1, config[kvRefKey1]); // Updated - short refresh interval
            Assert.Equal(secretValue2, config[kvRefKey2]);  // Not updated - long refresh interval
        }
    }
}
