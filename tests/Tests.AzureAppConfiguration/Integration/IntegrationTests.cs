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
        private const string DefaultLocation = "westus";
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
                Console.WriteLine($"Loading settings from {localSettingsPath}");
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
        /// Gets the endpoint for the App Configuration store.
        /// </summary>
        private Uri GetEndpoint()
        {
            return _appConfigEndpoint;
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
                SubscriptionResource subscription = _armClient.GetDefaultSubscription();

                // Create resource group if requested or use existing one
                if (createResourceGroup)
                {
                    _testResourceGroupName = $"appconfig-test-{Guid.NewGuid():N}".Substring(0, 20);
                    Console.WriteLine($"Creating temporary resource group: {_testResourceGroupName}");

                    var rgData = new ResourceGroupData(AzureLocation.Parse(location));
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
                Console.WriteLine($"Creating test App Configuration store: {_testStoreName}");

                // Create the App Configuration store
                var storeData = new AppConfigurationStoreData(AzureLocation.Parse(location), new AppConfigurationSku("free"));
                var createOperation = await _resourceGroup.GetAppConfigurationStores().CreateOrUpdateAsync(
                    WaitUntil.Completed,
                    _testStoreName,
                    storeData);

                _appConfigStore = createOperation.Value;
                _appConfigEndpoint = new Uri(_appConfigStore.Data.Endpoint);

                Console.WriteLine($"Store created: {_appConfigEndpoint}");

                // Initialize the configuration client for the store
                _configClient = new ConfigurationClient(_appConfigEndpoint, credential);

                // Add test settings to the store
                foreach (var setting in _testSettings)
                {
                    await _configClient.SetConfigurationSettingAsync(setting);
                }

                Console.WriteLine("Test data initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test initialization failed: {ex}");
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
                    Console.WriteLine($"Deleting test App Configuration store: {_testStoreName}");
                    await _appConfigStore.DeleteAsync(WaitUntil.Completed);
                    Console.WriteLine("Store deleted successfully");
                }

                if (_shouldDeleteResourceGroup && _resourceGroup != null)
                {
                    Console.WriteLine($"Deleting temporary resource group: {_testResourceGroupName}");
                    await _resourceGroup.DeleteAsync(WaitUntil.Completed);
                    Console.WriteLine("Resource group deleted successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test cleanup failed: {ex}. You may need to manually delete the resources: Store={_testStoreName}, ResourceGroup={(_shouldDeleteResourceGroup ? _testResourceGroupName : "N/A")}");
            }
        }

        [Fact]
        public void LoadConfiguration_RetrievesValuesFromAppConfiguration()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange & Act
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetEndpoint(), GetCredential());
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

            // Arrange
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetEndpoint(), GetCredential());
                    options.Select($"{TestKeyPrefix}:*");
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(SentinelKey, refreshAll: true)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{TestKeyPrefix}:Setting1"]);

            // Update values in the store
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{TestKeyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting(SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            var result = await refresher.TryRefreshAsync();

            // Assert
            Assert.True(result);
            Assert.Equal("UpdatedValue1", config[$"{TestKeyPrefix}:Setting1"]);
        }

        [Fact]
        public async Task TryRefreshAsync_RefreshesOnlySelectedKeys_WhenUsingKeyFilter()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetEndpoint(), GetCredential());
                    options.Select($"{TestKeyPrefix}:*");

                    // Only refresh Setting1 when sentinel changes
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(SentinelKey, $"{TestKeyPrefix}:Setting1", refreshAll: false)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{TestKeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{TestKeyPrefix}:Setting2"]);

            // Update values in the store
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{TestKeyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{TestKeyPrefix}:Setting2", "UpdatedValue2"));
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting(SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            var result = await refresher.TryRefreshAsync();

            // Assert
            Assert.True(result);
            Assert.Equal("UpdatedValue1", config[$"{TestKeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{TestKeyPrefix}:Setting2"]); // This value shouldn't change
        }

        [Fact]
        public async Task TryRefreshAsync_RefreshesFeatureFlags_WhenConfigured()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetEndpoint(), GetCredential());
                    options.Select($"{TestKeyPrefix}:*");
                    options.UseFeatureFlags();

                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(SentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial feature flag state
            Assert.Equal("False", config[$"FeatureManagement:{TestKeyPrefix}Feature:Enabled"]);

            // Update feature flag in the store
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    FeatureFlagKey,
                    @"{""id"":""" + TestKeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":true}",
                    contentType: FeatureManagementConstants.ContentType));
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting(SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            var result = await refresher.TryRefreshAsync();

            // Assert
            Assert.True(result);
            Assert.Equal("True", config[$"FeatureManagement:{TestKeyPrefix}Feature:Enabled"]);
        }

        [Fact]
        public async Task RegisterAll_RefreshesAllKeys_WhenSentinelChanged()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetEndpoint(), GetCredential());
                    options.Select($"{TestKeyPrefix}:*");

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
            Assert.Equal("InitialValue1", config[$"{TestKeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{TestKeyPrefix}:Setting2"]);

            // Update all values in the store
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{TestKeyPrefix}:Setting1", "UpdatedValue1"));
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{TestKeyPrefix}:Setting2", "UpdatedValue2"));
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting(SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            var result = await refresher.TryRefreshAsync();

            // Assert
            Assert.True(result);
            Assert.Equal("UpdatedValue1", config[$"{TestKeyPrefix}:Setting1"]);
            Assert.Equal("UpdatedValue2", config[$"{TestKeyPrefix}:Setting2"]);
        }

        [Fact]
        public async Task TryRefreshAsync_ReturnsFalse_WhenSentinelKeyUnchanged()
        {
            Skip.If(_skipTests, _skipReason);

            // Arrange
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetEndpoint(), GetCredential());
                    options.Select($"{TestKeyPrefix}:*");

                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(SentinelKey)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial values
            Assert.Equal("InitialValue1", config[$"{TestKeyPrefix}:Setting1"]);

            // Update data but not sentinel
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{TestKeyPrefix}:Setting1", "UpdatedValue1"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Act
            var result = await refresher.TryRefreshAsync();

            // Assert
            Assert.False(result); // Should return false as sentinel hasn't changed
            Assert.Equal("InitialValue1", config[$"{TestKeyPrefix}:Setting1"]); // Should not update
        }
    }
}
