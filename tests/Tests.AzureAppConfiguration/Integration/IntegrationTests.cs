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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    /// <summary>
    /// Integration tests for Azure App Configuration that connect to a real service.
    /// Creates a temporary App Configuration store for testing and deletes it after the tests are complete.
    /// Requires Azure credentials with appropriate permissions.
    /// </summary>
    [Trait("Category", "Integration")]
    [CollectionDefinition(nameof(IntegrationTests), DisableParallelization = true)]
    public class IntegrationTests : IAsyncLifetime
    {
        // Test constants
        private const string TestKeyPrefix = "IntegrationTest";
        private const string SentinelKey = TestKeyPrefix + ":Sentinel";
        private const string FeatureFlagKey = ".appconfig.featureflag/" + TestKeyPrefix + "Feature";

        // Azure Resource Management constants
        private const string ResourceGroupEnvVar = "AZURE_APPCONFIG_RESOURCE_GROUP";
        private const string SubscriptionIdEnvVar = "AZURE_SUBSCRIPTION_ID";
        private const string LocationEnvVar = "AZURE_LOCATION";
        private const string CreateResourceGroupEnvVar = "AZURE_CREATE_RESOURCE_GROUP";
        private const string DefaultLocation = "eastus";
        private const string LocalSettingsFile = "local.settings.json";

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
        private bool _shouldDeleteResourceGroup = false;
        private AppConfigurationStoreResource _appConfigStore;
        private Uri _appConfigEndpoint;
        private ResourceGroupResource _resourceGroup;

        // Flag indicating whether tests should run
        private bool _skipTests = false;
        private string _skipReason = null;

        /// <summary>
        /// Loads environment variables from a local settings file if it exists.
        /// </summary>
        private void LoadEnvironmentVariablesFromFile()
        {
            string localSettingsPath = Path.Combine(AppContext.BaseDirectory, LocalSettingsFile);

            if (File.Exists(localSettingsPath))
            {
                try
                {
                    var config = new ConfigurationBuilder()
                        .AddJsonFile(localSettingsPath, optional: true)
                        .Build();

                    foreach (var setting in config.AsEnumerable())
                    {
                        if (!string.IsNullOrEmpty(setting.Value) &&
                            Environment.GetEnvironmentVariable(setting.Key) == null)
                        {
                            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading local settings: {ex.Message}");
                }
            }
        }

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
                // Load environment variables from local.settings.json if present
                LoadEnvironmentVariablesFromFile();

                var credential = GetCredential();
                if (_skipTests) return;

                // Get required Azure information from environment variables
                string resourceGroupName = Environment.GetEnvironmentVariable(ResourceGroupEnvVar);
                string subscriptionIdStr = Environment.GetEnvironmentVariable(SubscriptionIdEnvVar);
                string location = Environment.GetEnvironmentVariable(LocationEnvVar) ?? DefaultLocation;
                bool createResourceGroup = string.Equals(Environment.GetEnvironmentVariable(CreateResourceGroupEnvVar), "true", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(subscriptionIdStr))
                {
                    _skipTests = true;
                    _skipReason = $"Missing required environment variable: {SubscriptionIdEnvVar}";
                    return;
                }

                // Initialize Azure Resource Manager client
                _armClient = new ArmClient(credential);

                SubscriptionResource subscription = _armClient.GetSubscriptions().Get(subscriptionIdStr);

                // Create resource group if requested or use existing one
                if (createResourceGroup)
                {
                    _testResourceGroupName = $"appconfig-test-{Guid.NewGuid():N}".Substring(0, 20);

                    var rgData = new ResourceGroupData(new AzureLocation(location));
                    var rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, _testResourceGroupName, rgData);
                    _resourceGroup = rgLro.Value;
                    _shouldDeleteResourceGroup = true;
                }
                else
                {
                    if (string.IsNullOrEmpty(resourceGroupName))
                    {
                        _skipTests = true;
                        _skipReason = $"Missing required environment variable: {ResourceGroupEnvVar}";
                        return;
                    }

                    _testResourceGroupName = resourceGroupName;
                    _resourceGroup = await subscription.GetResourceGroups().GetAsync(resourceGroupName);
                }

                // Create unique store name for this test run
                _testStoreName = $"integration-{Guid.NewGuid():N}".Substring(0, 20);

                // Create the App Configuration store
                var storeData = new AppConfigurationStoreData(new AzureLocation(location), new AppConfigurationSku("free"));
                var createOperation = await _resourceGroup.GetAppConfigurationStores().CreateOrUpdateAsync(
                    WaitUntil.Completed,
                    _testStoreName,
                    storeData);

                _appConfigStore = createOperation.Value;
                _appConfigEndpoint = new Uri(_appConfigStore.Data.Endpoint);

                // Get the connection string for the store instead of using RBAC
                var accessKeys = _appConfigStore.GetKeysAsync();
                var primaryKey = await accessKeys.FirstOrDefaultAsync();

                if (primaryKey == null)
                {
                    throw new InvalidOperationException("Failed to retrieve access keys from App Configuration store.");
                }

                _connectionString = primaryKey.ConnectionString;

                // Initialize the configuration client with the connection string
                _configClient = new ConfigurationClient(_connectionString);

                // Add test settings to the store
                foreach (var setting in _testSettings)
                {
                    await _configClient.SetConfigurationSettingAsync(setting);
                }
            }
            catch (Exception ex)
            {
                _skipTests = true;
                _skipReason = $"Failed to initialize integration tests: {ex.Message}";

                // Clean up any partially created resources
                await DisposeAsync();
            }
        }

        /// <summary>
        /// Cleans up the temporary App Configuration store after tests are complete.
        /// </summary>
        public async Task DisposeAsync()
        {
            // Don't attempt cleanup if we don't have a store to delete
            if (_appConfigStore == null && !_shouldDeleteResourceGroup)
            {
                return;
            }

            try
            {
                if (_appConfigStore != null)
                {
                    await _appConfigStore.DeleteAsync(WaitUntil.Completed);
                }

                if (_shouldDeleteResourceGroup && _resourceGroup != null)
                {
                    await _resourceGroup.DeleteAsync(WaitUntil.Completed);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test cleanup failed: {ex}. You may need to manually delete the resources: Store={_testStoreName}, ResourceGroup={(_shouldDeleteResourceGroup ? _testResourceGroupName : "N/A")}");
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
        private async Task<(string keyPrefix, string sentinelKey, string featureFlagKey)> SetupTestKeys(string testName)
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

            return (keyPrefix, sentinelKey, featureFlagKey);
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
        public async Task TryRefreshAsync_UpdatesConfiguration_WhenSentinelKeyChanged()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange - Setup test-specific keys
            var (keyPrefix, sentinelKey, _) = await SetupTestKeys("UpdatesConfig");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{keyPrefix}:*");
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(sentinelKey, refreshAll: true)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{keyPrefix}:Setting1"]);

            // Update values in the store
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{keyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(sentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            var result = await refresher.TryRefreshAsync();

            // Assert
            Assert.True(result);
            Assert.Equal("UpdatedValue1", config[$"{keyPrefix}:Setting1"]);
        }

        [Fact]
        public async Task TryRefreshAsync_RefreshesOnlySelectedKeys_WhenUsingKeyFilter()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange - Setup test-specific keys
            var (keyPrefix, sentinelKey, _) = await SetupTestKeys("RefreshesSelectedKeys");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{keyPrefix}:*");

                    // Only refresh Setting1 when sentinel changes
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(sentinelKey, $"{keyPrefix}:Setting1", refreshAll: false)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{keyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{keyPrefix}:Setting2"]);

            // Update values in the store
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{keyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{keyPrefix}:Setting2", "UpdatedValue2"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(sentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            var result = await refresher.TryRefreshAsync();

            // Assert
            Assert.True(result);
            Assert.Equal("UpdatedValue1", config[$"{keyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{keyPrefix}:Setting2"]); // This value shouldn't change
        }

        [Fact]
        public async Task TryRefreshAsync_RefreshesFeatureFlags_WhenConfigured()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange - Setup test-specific keys
            var (keyPrefix, sentinelKey, featureFlagKey) = await SetupTestKeys("RefreshesFeatureFlags");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{keyPrefix}:*");
                    options.UseFeatureFlags();

                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(sentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial feature flag state
            Assert.Equal("False", config[$"FeatureManagement:{keyPrefix}Feature:Enabled"]);

            // Update feature flag in the store
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    featureFlagKey,
                    @"{""id"":""" + keyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":true}",
                    contentType: FeatureManagementConstants.ContentType));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(sentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            var result = await refresher.TryRefreshAsync();

            // Assert
            Assert.True(result);
            Assert.Equal("True", config[$"FeatureManagement:{keyPrefix}Feature:Enabled"]);
        }

        [Fact]
        public async Task RegisterAll_RefreshesAllKeys_WhenSentinelChanged()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange - Setup test-specific keys
            var (keyPrefix, sentinelKey, _) = await SetupTestKeys("RefreshesAllKeys");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{keyPrefix}:*");

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
            Assert.Equal("InitialValue1", config[$"{keyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{keyPrefix}:Setting2"]);

            // Update all values in the store
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{keyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{keyPrefix}:Setting2", "UpdatedValue2"));
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(sentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            var result = await refresher.TryRefreshAsync();

            // Assert
            Assert.True(result);
            Assert.Equal("UpdatedValue1", config[$"{keyPrefix}:Setting1"]);
            Assert.Equal("UpdatedValue2", config[$"{keyPrefix}:Setting2"]);
        }

        [Fact]
        public async Task TryRefreshAsync_ReturnsFalse_WhenSentinelKeyUnchanged()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange - Setup test-specific keys
            var (keyPrefix, sentinelKey, _) = await SetupTestKeys("SentinelUnchanged");
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{keyPrefix}:*");

                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(sentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{keyPrefix}:Setting1"]);

            // Update data but not sentinel
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{keyPrefix}:Setting1", "UpdatedValue1"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            var result = await refresher.TryRefreshAsync();

            // Assert
            Assert.False(result); // Should return false as sentinel hasn't changed
            Assert.Equal("InitialValue1", config[$"{keyPrefix}:Setting1"]); // Should not update
        }
    }
}
