using Azure;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    /// <summary>
    /// Integration tests for Azure App Configuration that connect to a real service.
    /// Requires valid connection details to be provided through environment variables or other secure methods.
    /// </summary>
    [Trait("Category", "Integration")]
    public class IntegrationTests : IAsyncLifetime
    {
        // Test constants
        private const string TestKeyPrefix = "IntegrationTest";
        private const string SentinelKey = TestKeyPrefix + ":Sentinel";
        private const string FeatureFlagKey = ".appconfig.featureflag/" + TestKeyPrefix + "Feature";

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

        /// <summary>
        /// Gets a DefaultAzureCredential for authentication (alternative to connection string).
        /// </summary>
        private DefaultAzureCredential GetCredential()
        {
            return new DefaultAzureCredential();
        }

        /// <summary>
        /// Gets the endpoint for the App Configuration store.
        /// </summary>
        private Uri GetEndpoint()
        {
            return new Uri(Environment.GetEnvironmentVariable("AZURE_APPCONFIG_INTEGRATIONTEST_ENDPOINT"));
        }

        /// <summary>
        /// Creates test data in the Azure App Configuration store before running tests.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                // Get endpoint from environment variable
                string endpoint = Environment.GetEnvironmentVariable("AZURE_APPCONFIG_INTEGRATIONTEST_ENDPOINT");

                if (string.IsNullOrEmpty(endpoint))
                {
                    throw new InvalidOperationException("AZURE_APPCONFIG_INTEGRATIONTEST_ENDPOINT environment variable is required when using managed identity");
                }

                _configClient = new ConfigurationClient(new Uri(endpoint), GetCredential());

                // Add test settings to the store
                foreach (var setting in _testSettings)
                {
                    await _configClient.SetConfigurationSettingAsync(setting);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test initialization failed: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Cleans up test data after tests are complete.
        /// </summary>
        public async Task DisposeAsync()
        {
            try
            {
                // Remove test settings from the store
                foreach (var setting in _testSettings)
                {
                    await _configClient.DeleteConfigurationSettingAsync(setting.Key, setting.Label);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test cleanup failed: {ex}");
            }
        }

        [Fact]
        public void LoadConfiguration_RetrievesValuesFromAppConfiguration()
        {
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
                              .SetCacheExpiration(TimeSpan.FromSeconds(1));
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
                              .SetCacheExpiration(TimeSpan.FromSeconds(1));
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
                              .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });
                    
                    refresher = options.GetRefresher();
                })
                .Build();
            
            // Verify initial feature flag state
            Assert.Equal("False", config[$"FeatureManagement:{TestKeyPrefix}Feature:Enabled"]);
            
            // Update feature flag in the store
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting(
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
                        refresh.RegisterAll(SentinelKey)
                              .SetCacheExpiration(TimeSpan.FromSeconds(1));
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
                              .SetCacheExpiration(TimeSpan.FromSeconds(1));
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
