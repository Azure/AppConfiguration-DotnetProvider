// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppConfiguration;
using Azure.ResourceManager.AppConfiguration.Models;
using Azure.ResourceManager.KeyVault;
using Azure.ResourceManager.Resources;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.SnapshotReference;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.FeatureManagement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    using Xunit.Abstractions;

    /// <summary>
    /// Integration tests for Azure App Configuration that connect to a real service.
    /// Uses an existing App Configuration store and Key Vault for testing.
    /// Requires Azure credentials with appropriate permissions.
    /// NOTE: Before running these tests locally, execute the GetAzureSubscription.ps1 script to create appsettings.Secrets.json.
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

        // Content type constants
        private const string FeatureFlagContentType = FeatureManagementConstants.ContentType + ";charset=utf-8";
        private const string JsonContentType = "application/json";

        // Fixed resource names - already existing
        private const string AppConfigStoreName = "appconfig-dotnetprovider-integrationtest";
        private const string KeyVaultName = "keyvault-dotnetprovider";
        private const string ResourceGroupName = "dotnetprovider-integrationtest";

        private readonly DefaultAzureCredential _defaultAzureCredential = new DefaultAzureCredential(
            new DefaultAzureCredentialOptions
            {
                ExcludeSharedTokenCacheCredential = true
            });

        private class TestContext
        {
            public string KeyPrefix { get; set; }
            public string SentinelKey { get; set; }
            public string FeatureFlagKey { get; set; }
            public string KeyVaultReferenceKey { get; set; }
            public string SecretValue { get; set; }
            public string SnapshotReferenceKey { get; set; }
            public string SnapshotName { get; set; }
        }

        private ConfigurationClient _configClient;

        private SecretClient _secretClient;

        private string _connectionString;

        private Uri _keyVaultEndpoint;

        private readonly ITestOutputHelper _output;

        public IntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

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

            return root.GetProperty("SubscriptionId").GetString();
        }

        private string GetUniqueKeyPrefix(string testName)
        {
            // Use a combination of the test prefix and test method name to ensure uniqueness
            return $"{TestKeyPrefix}-{testName}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        private async Task<string> CreateSnapshot(string snapshotName, IEnumerable<ConfigurationSettingsFilter> settingsToInclude, SnapshotComposition snapshotComposition, CancellationToken cancellationToken = default)
        {
            ConfigurationSnapshot snapshot = new ConfigurationSnapshot(settingsToInclude);

            snapshot.SnapshotComposition = snapshotComposition;
            snapshot.RetentionPeriod = TimeSpan.FromHours(1);

            CreateSnapshotOperation operation = await _configClient.CreateSnapshotAsync(
                WaitUntil.Completed,
                snapshotName,
                snapshot,
                cancellationToken);

            return operation.Value.Name;
        }

        public async Task InitializeAsync()
        {
            DefaultAzureCredential credential = _defaultAzureCredential;
            string subscriptionId = GetCurrentSubscriptionId();

            var armClient = new ArmClient(credential);
            SubscriptionResource subscription = armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}"));

            ResourceGroupResource resourceGroup = await subscription.GetResourceGroups().GetAsync(ResourceGroupName);

            AppConfigurationStoreResource appConfigStore = null;

            try
            {
                appConfigStore = await resourceGroup.GetAppConfigurationStores().GetAsync(AppConfigStoreName);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                throw new InvalidOperationException($"App Configuration store '{AppConfigStoreName}' not found in resource group '{ResourceGroupName}'. Please create it before running tests.", ex);
            }

            AsyncPageable<AppConfigurationStoreApiKey> accessKeys = appConfigStore.GetKeysAsync();

            _connectionString = (await accessKeys.FirstAsync()).ConnectionString;

            _configClient = new ConfigurationClient(_connectionString);

            KeyVaultResource keyVault = null;

            // Find and initialize Key Vault - look in the same resource group
            keyVault = await resourceGroup.GetKeyVaults().GetAsync(KeyVaultName);

            if (keyVault == null)
            {
                throw new InvalidOperationException(
                    $"Key Vault '{KeyVaultName}' not found in subscription {subscriptionId}. " +
                    "This resource is required for integration tests. " +
                    "Please create the Key Vault with the appropriate permissions before running tests.");
            }

            _keyVaultEndpoint = keyVault.Data.Properties.VaultUri;

            _secretClient = new SecretClient(_keyVaultEndpoint, credential);

            _output.WriteLine($"Successfully connected to App Configuration store '{AppConfigStoreName}' and Key Vault '{KeyVaultName}'");
        }

        private async Task CleanupStaleResources()
        {
            _output.WriteLine($"Checking for stale resources older than {StaleResourceThreshold}...");

            var cutoffTime = DateTimeOffset.UtcNow.Subtract(StaleResourceThreshold);

            // Clean up stale configuration settings, snapshots, and Key Vault secrets
            try
            {
                int staleConfigCount = 0;
                var configSettingsToCleanup = new List<ConfigurationSetting>();

                AsyncPageable<ConfigurationSetting> kvSettings = _configClient.GetConfigurationSettingsAsync(new SettingSelector
                {
                    KeyFilter = TestKeyPrefix + "*"
                });

                AsyncPageable<ConfigurationSetting> flagSettings = _configClient.GetConfigurationSettingsAsync(new SettingSelector
                {
                    KeyFilter = FeatureManagementConstants.FeatureFlagMarker + TestKeyPrefix + "*"
                });

                await foreach (ConfigurationSetting setting in kvSettings.Concat(flagSettings))
                {
                    // Check if the setting is older than the threshold
                    if (setting.LastModified < cutoffTime)
                    {
                        configSettingsToCleanup.Add(setting);
                        staleConfigCount++;
                    }
                }

                // Delete configuration settings sequentially to avoid 429 errors
                foreach (ConfigurationSetting setting in configSettingsToCleanup)
                {
                    await _configClient.DeleteConfigurationSettingAsync(setting.Key, setting.Label);
                }

                int staleSnapshotCount = 0;
                var snapshotsToArchive = new List<string>();
                AsyncPageable<ConfigurationSnapshot> snapshots = _configClient.GetSnapshotsAsync(new SnapshotSelector());
                await foreach (ConfigurationSnapshot snapshot in snapshots)
                {
                    if (snapshot.Name.StartsWith("snapshot-" + TestKeyPrefix) && snapshot.CreatedOn < cutoffTime && snapshot.Status == ConfigurationSnapshotStatus.Ready)
                    {
                        snapshotsToArchive.Add(snapshot.Name);
                        staleSnapshotCount++;
                    }
                }

                // Archive snapshots sequentially to avoid 429 errors
                foreach (string snapshotName in snapshotsToArchive)
                {
                    await _configClient.ArchiveSnapshotAsync(snapshotName);
                }

                int staleSecretCount = 0;
                var secretsToDelete = new List<string>();
                if (_secretClient != null)
                {
                    AsyncPageable<SecretProperties> secrets = _secretClient.GetPropertiesOfSecretsAsync();
                    await foreach (SecretProperties secretProperties in secrets)
                    {
                        if (secretProperties.Name.StartsWith(TestKeyPrefix) && secretProperties.CreatedOn.HasValue && secretProperties.CreatedOn.Value < cutoffTime)
                        {
                            secretsToDelete.Add(secretProperties.Name);
                            staleSecretCount++;
                        }
                    }

                    // Delete secrets sequentially to avoid 429 errors
                    foreach (string secretName in secretsToDelete)
                    {
                        await _secretClient.StartDeleteSecretAsync(secretName);
                    }
                }

                _output.WriteLine($"Cleaned up {staleConfigCount} stale configuration settings, {staleSnapshotCount} snapshots, and {staleSecretCount} secrets");
            }
            catch (RequestFailedException ex)
            {
                _output.WriteLine($"Error during stale resource cleanup: {ex.Message}");
                // Continue execution even if cleanup fails
            }
        }

        public async Task DisposeAsync()
        {
            await CleanupStaleResources();
        }

        private TestContext CreateTestContext(string testName)
        {
            string keyPrefix = GetUniqueKeyPrefix(testName);
            string sentinelKey = $"{keyPrefix}:Sentinel";
            string featureFlagKey = $".appconfig.featureflag/{keyPrefix}Feature";
            string secretName = $"{keyPrefix}-secret";
            string secretValue = "SecretValue";
            string keyVaultReferenceKey = $"{keyPrefix}:KeyVaultRef";
            string snapshotReferenceKey = $"{keyPrefix}:SnapshotRef";
            string snapshotName = $"{keyPrefix}-snapshot";

            return new TestContext
            {
                KeyPrefix = keyPrefix,
                SentinelKey = sentinelKey,
                FeatureFlagKey = featureFlagKey,
                KeyVaultReferenceKey = keyVaultReferenceKey,
                SecretValue = secretValue,
                SnapshotReferenceKey = snapshotReferenceKey,
                SnapshotName = snapshotName
            };
        }

        private async Task SetupKeyValues(TestContext context)
        {
            var testSettings = new List<ConfigurationSetting>
            {
                new ConfigurationSetting($"{context.KeyPrefix}:Setting1", "InitialValue1"),
                new ConfigurationSetting($"{context.KeyPrefix}:Setting2", "InitialValue2"),
                new ConfigurationSetting(context.SentinelKey, "Initial")
            };

            foreach (ConfigurationSetting setting in testSettings)
            {
                await _configClient.SetConfigurationSettingAsync(setting);
            }
        }

        private async Task SetupFeatureFlags(TestContext context)
        {
            var featureFlagSetting = ConfigurationModelFactory.ConfigurationSetting(
                context.FeatureFlagKey,
                @"{""id"":""" + context.KeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":false}",
                contentType: FeatureFlagContentType);

            await _configClient.SetConfigurationSettingAsync(featureFlagSetting);
        }

        private async Task SetupKeyVaultReferences(TestContext context)
        {
            if (_secretClient != null)
            {
                await _secretClient.SetSecretAsync(context.KeyPrefix + "-secret", context.SecretValue);

                string keyVaultUri = $"{_keyVaultEndpoint}secrets/{context.KeyPrefix}-secret";
                string keyVaultRefValue = @$"{{""uri"":""{keyVaultUri}""}}";

                ConfigurationSetting keyVaultRefSetting = ConfigurationModelFactory.ConfigurationSetting(
                    context.KeyVaultReferenceKey,
                    keyVaultRefValue,
                    label: KeyVaultReferenceLabel,
                    contentType: KeyVaultConstants.ContentType);

                await _configClient.SetConfigurationSettingAsync(keyVaultRefSetting);
            }
        }

        private async Task SetUpSnapshotReferences(TestContext context)
        {
            if (_configClient != null)
            {
                var settingsToInclude = new List<ConfigurationSettingsFilter>
                {
                    new ConfigurationSettingsFilter(context.KeyPrefix + ":*")
                };
                await CreateSnapshot(context.SnapshotName, settingsToInclude, SnapshotComposition.Key);

                ConfigurationSetting snapshotReferenceSetting = ConfigurationModelFactory.ConfigurationSetting(
                    context.SnapshotReferenceKey,
                    JsonSerializer.Serialize(new { snapshot_name = context.SnapshotName }),
                    contentType: SnapshotReferenceConstants.ContentType
                );
                await _configClient.SetConfigurationSettingAsync(snapshotReferenceSetting);
            }
        }

        private async Task SetupTaggedSettings(TestContext context)
        {
            // Create configuration settings with various tags
            var taggedSettings = new List<ConfigurationSetting>
            {
                // Basic environment tags
                CreateSettingWithTags(
                    $"{context.KeyPrefix}:TaggedSetting1",
                    "Value1",
                    new Dictionary<string, string> {
                        { "Environment", "Development" },
                        { "App", "TestApp" }
                    }),

                CreateSettingWithTags(
                    $"{context.KeyPrefix}:TaggedSetting2",
                    "Value2",
                    new Dictionary<string, string> {
                        { "Environment", "Production" },
                        { "App", "TestApp" }
                    }),

                CreateSettingWithTags(
                    $"{context.KeyPrefix}:TaggedSetting3",
                    "Value3",
                    new Dictionary<string, string> {
                        { "Environment", "Development" },
                        { "Component", "API" }
                    }),
                
                // Special characters in tags
                CreateSettingWithTags(
                    $"{context.KeyPrefix}:TaggedSetting4",
                    "Value4",
                    new Dictionary<string, string> {
                        { "Special:Tag", "Value:With:Colons" },
                        { "Tag@With@At", "Value@With@At" }
                    }),
                
                // Empty and null tag values
                CreateSettingWithTags(
                    $"{context.KeyPrefix}:TaggedSetting5",
                    "Value5",
                    new Dictionary<string, string> {
                        { "EmptyTag", "" },
                        { "NullTag", null }
                    })
            };

            foreach (ConfigurationSetting setting in taggedSettings)
            {
                await _configClient.SetConfigurationSettingAsync(setting);
            }

            // Create feature flags with tags
            var taggedFeatureFlags = new List<ConfigurationSetting>
            {
                // Basic environment tags on feature flags
                CreateFeatureFlagWithTags(
                    $"{context.KeyPrefix}:FeatureDev",
                    true,
                    new Dictionary<string, string> {
                        { "Environment", "Development" },
                        { "App", "TestApp" }
                    }),

                CreateFeatureFlagWithTags(
                    $"{context.KeyPrefix}:FeatureProd",
                    false,
                    new Dictionary<string, string> {
                        { "Environment", "Production" },
                        { "App", "TestApp" }
                    }),
                
                // Feature flags with special character tags
                CreateFeatureFlagWithTags(
                    $"{context.KeyPrefix}:FeatureSpecial",
                    true,
                    new Dictionary<string, string> {
                        { "Special:Tag", "Value:With:Colons" }
                    }),
                
                // Feature flags with empty/null tags
                CreateFeatureFlagWithTags(
                    $"{context.KeyPrefix}:FeatureEmpty",
                    false,
                    new Dictionary<string, string> {
                        { "EmptyTag", "" },
                        { "NullTag", null }
                    })
            };

            foreach (ConfigurationSetting setting in taggedFeatureFlags)
            {
                await _configClient.SetConfigurationSettingAsync(setting);
            }
        }

        private async Task<TestContext> SetupAllTestData(string testName)
        {
            TestContext context = CreateTestContext(testName);

            await SetupKeyValues(context);
            await SetupFeatureFlags(context);
            await SetupKeyVaultReferences(context);
            await SetUpSnapshotReferences(context);

            return context;
        }

        [Fact]
        public async Task LoadConfiguration_RetrievesValuesFromAppConfiguration()
        {
            // Arrange - Setup test-specific keys
            TestContext testContext = CreateTestContext("BasicConfig");
            await SetupKeyValues(testContext);

            // Act
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
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
            TestContext testContext = CreateTestContext("UpdatesConfig");
            await SetupKeyValues(testContext);
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
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
            TestContext testContext = CreateTestContext("RefreshesAllKeys");
            await SetupKeyValues(testContext);
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
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
            TestContext testContext = CreateTestContext("SentinelUnchanged");
            await SetupKeyValues(testContext);
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
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
            TestContext testContext = CreateTestContext("FeatureFlagRefresh");
            await SetupFeatureFlags(testContext);
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.ConfigureKeyVault(kv => kv.SetCredential(_defaultAzureCredential));

                    // Configure feature flags with the correct ID pattern
                    options.UseFeatureFlags(featureFlagOptions =>
                    {
                        featureFlagOptions.Select(testContext.KeyPrefix + "*");
                        featureFlagOptions.SetRefreshInterval(TimeSpan.FromSeconds(1));
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
                    contentType: FeatureFlagContentType));

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
            TestContext testContext = CreateTestContext("FeatureFlagFilters");
            await SetupFeatureFlags(testContext);

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
                    contentType: FeatureFlagContentType));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.ConfigureKeyVault(kv => kv.SetCredential(_defaultAzureCredential));
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
            TestContext testContext1 = CreateTestContext("MultiProviderTest1");
            await SetupKeyValues(testContext1);
            TestContext testContext2 = CreateTestContext("MultiProviderTest2");
            await SetupKeyValues(testContext2);
            IConfigurationRefresher refresher1 = null;
            IConfigurationRefresher refresher2 = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
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
                    options.Connect(_connectionString);
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
            TestContext testContext = CreateTestContext("FeatureFlagVariants");
            await SetupFeatureFlags(testContext);

            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":true}",
                    contentType: FeatureFlagContentType));

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
                    contentType: FeatureFlagContentType));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.ConfigureKeyVault(kv => kv.SetCredential(_defaultAzureCredential));
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
            TestContext testContext = CreateTestContext("JsonContent");
            await SetupKeyValues(testContext);

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
                    contentType: JsonContentType));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
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
            TestContext testContext = CreateTestContext("MethodOrdering");
            await SetupKeyValues(testContext);
            await SetupFeatureFlags(testContext);

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
                    contentType: FeatureFlagContentType));

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
                    options.Connect(_connectionString);
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
                    options.Connect(_connectionString);
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
                    options.Connect(_connectionString);
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
                    options.Connect(_connectionString);
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
                    contentType: FeatureFlagContentType));

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
            TestContext testContext = CreateTestContext("RefreshEquivalency");
            await SetupKeyValues(testContext);
            await SetupFeatureFlags(testContext);

            // Add another feature flag for testing
            string secondFeatureFlagKey = $".appconfig.featureflag/{testContext.KeyPrefix}Feature2";
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    secondFeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature2"",""description"":""Second test feature"",""enabled"":false}",
                    contentType: FeatureFlagContentType));

            // Create two separate configuration builders with different refresh methods
            // First configuration uses Register with refreshAll: true
            IConfigurationRefresher refresher1 = null;
            var config1 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
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
                    options.Connect(_connectionString);
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
                    contentType: FeatureFlagContentType));

            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    secondFeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature2"",""description"":""Second test feature"",""enabled"":true}",
                    contentType: FeatureFlagContentType));

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
            TestContext testContext = CreateTestContext("FailoverStartup");
            await SetupKeyValues(testContext);
            IConfigurationRefresher refresher = null;

            string connectionString = _connectionString;

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

        [Fact]
        public async Task LoadSnapshot_RetrievesValuesFromSnapshot()
        {
            // Arrange - Setup test-specific keys
            TestContext testContext = CreateTestContext("SnapshotTest");
            await SetupKeyValues(testContext);
            string snapshotName = $"snapshot-{testContext.KeyPrefix}";

            // Create a snapshot with the test keys
            await CreateSnapshot(snapshotName, new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter(testContext.KeyPrefix + "*") }, SnapshotComposition.Key);

            // Update values after snapshot is taken to verify snapshot has original values
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting1", "UpdatedAfterSnapshot"));

            // Act - Load configuration from snapshot
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.SelectSnapshot(snapshotName);
                })
                .Build();

            // Assert - Should have original values from snapshot, not updated values
            Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]);

            // Cleanup - Delete the snapshot
            await _configClient.ArchiveSnapshotAsync(snapshotName);
        }

        [Fact]
        public void LoadSnapshot_ReturnsEmpty_WhenSnapshotDoesNotExist()
        {
            // Arrange - Setup test-specific keys
            TestContext testContext = CreateTestContext("NonExistentSnapshotTest");
            string nonExistentSnapshotName = $"snapshot-does-not-exist-{Guid.NewGuid()}";

            // Act - Loading a non-existent snapshot should return empty configuration
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.SelectSnapshot(nonExistentSnapshotName);
                })
                .Build();

            // Assert - Configuration should be empty (no settings loaded from non-existent snapshot)
            Assert.Empty(config.AsEnumerable());
        }

        [Fact]
        public async Task LoadMultipleSnapshots_MergesConfigurationCorrectly()
        {
            // Arrange - Setup test-specific keys for two separate snapshots
            TestContext testContext1 = CreateTestContext("SnapshotMergeTest1");
            await SetupKeyValues(testContext1);
            TestContext testContext2 = CreateTestContext("SnapshotMergeTest2");
            await SetupKeyValues(testContext2);

            // Create specific values for second snapshot
            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{testContext2.KeyPrefix}:UniqueKey", "UniqueValue"));

            string snapshotName1 = $"snapshot-{testContext1.KeyPrefix}";
            string snapshotName2 = $"snapshot-{testContext2.KeyPrefix}";

            // Create snapshots
            await CreateSnapshot(snapshotName1, new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter(testContext1.KeyPrefix + "*") }, SnapshotComposition.Key);
            await CreateSnapshot(snapshotName2, new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter(testContext2.KeyPrefix + "*") }, SnapshotComposition.Key);

            try
            {
                // Act - Load configuration from both snapshots
                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(_connectionString);
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

        [Fact]
        public async Task SnapshotCompositionTypes_AreHandledCorrectly()
        {
            // Arrange - Setup test-specific keys
            TestContext testContext = CreateTestContext("SnapshotCompositionTest");
            await SetupKeyValues(testContext);
            string keyOnlySnapshotName = $"snapshot-key-{testContext.KeyPrefix}";
            string invalidCompositionSnapshotName = $"snapshot-invalid-{testContext.KeyPrefix}";

            // Create a snapshot with the test keys
            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter($"{testContext.KeyPrefix}:*")
            };

            // Create the snapshot
            await CreateSnapshot(keyOnlySnapshotName, settingsToInclude, SnapshotComposition.Key);

            // Create the snapshot
            await CreateSnapshot(invalidCompositionSnapshotName, settingsToInclude, SnapshotComposition.KeyLabel);

            try
            {
                // Act & Assert - Loading a key-only snapshot should work
                var config1 = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(_connectionString);
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
                            options.Connect(_connectionString);
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

        [Fact]
        public async Task SnapshotWithFeatureFlags_LoadsConfigurationCorrectly()
        {
            // Arrange - Setup test-specific keys
            TestContext testContext = CreateTestContext("SnapshotFeatureFlagTest");
            await SetupFeatureFlags(testContext);
            string snapshotName = $"snapshot-ff-{testContext.KeyPrefix}";

            // Update the feature flag to be enabled before creating the snapshot
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":true}",
                    contentType: FeatureFlagContentType));

            // Create a snapshot with the test keys
            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter($".appconfig.featureflag/{testContext.KeyPrefix}*")
            };

            // Create the snapshot
            await CreateSnapshot(snapshotName, settingsToInclude, SnapshotComposition.Key);

            // Update feature flag to disabled after creating snapshot
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{""id"":""" + testContext.KeyPrefix + @"Feature"",""description"":""Test feature"",""enabled"":false}",
                    contentType: FeatureFlagContentType));

            try
            {
                // Act - Load configuration from snapshot with feature flags
                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(_connectionString);
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

        [Fact]
        public async Task CallOrdering_SnapshotsWithSelectAndFeatureFlags()
        {
            // Arrange - Setup test-specific keys for multiple snapshots
            TestContext mainContext = CreateTestContext("SnapshotOrdering");
            await SetupKeyValues(mainContext);
            await SetupFeatureFlags(mainContext);

            TestContext secondContext = CreateTestContext("SnapshotOrdering2");
            await SetupKeyValues(secondContext);
            await SetupFeatureFlags(secondContext);

            TestContext thirdContext = CreateTestContext("SnapshotOrdering3");
            await SetupKeyValues(thirdContext);

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
                    contentType: FeatureFlagContentType));

            string thirdFeatureFlagKey = $".appconfig.featureflag/{secondContext.KeyPrefix}Feature";
            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    thirdFeatureFlagKey,
                    @"{""id"":""" + secondContext.KeyPrefix + @"Feature"",""description"":""Third test feature"",""enabled"":true}",
                    contentType: FeatureFlagContentType));

            // Create snapshots
            string snapshot1 = $"snapshot-{mainContext.KeyPrefix}-1";
            string snapshot2 = $"snapshot-{secondContext.KeyPrefix}-2";
            string snapshot3 = $"snapshot-{thirdContext.KeyPrefix}-3";

            await CreateSnapshot(snapshot1, new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter(mainContext.KeyPrefix + "*") }, SnapshotComposition.Key);
            await CreateSnapshot(snapshot2, new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter(secondContext.KeyPrefix + "*") }, SnapshotComposition.Key);
            await CreateSnapshot(snapshot3, new List<ConfigurationSettingsFilter> { new ConfigurationSettingsFilter(thirdContext.KeyPrefix + "*") }, SnapshotComposition.Key);

            try
            {
                // Test different orderings of SelectSnapshot, Select and UseFeatureFlags

                // Order 1: SelectSnapshot -> Select -> UseFeatureFlags
                var config1 = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(_connectionString);
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
                        options.Connect(_connectionString);
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
                        options.Connect(_connectionString);
                        options.UseFeatureFlags();
                        options.SelectSnapshot(snapshot3);
                        options.Select($"{thirdContext.KeyPrefix}:*");
                    })
                    .Build();

                // Order 4: Multiple snapshots with interleaved operations
                var config4 = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(_connectionString);
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

        [Fact]
        public async Task KeyVaultReferences_ResolveCorrectly()
        {
            // Arrange - Setup test-specific keys
            TestContext testContext = CreateTestContext("KeyVaultReference");
            await SetupKeyVaultReferences(testContext);

            // Act - Create configuration with Key Vault support
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{testContext.KeyPrefix}:*", KeyVaultReferenceLabel);
                    options.ConfigureKeyVault(kv => kv.SetCredential(_defaultAzureCredential));
                })
                .Build();

            // Assert - Key Vault reference should be resolved to the secret value
            Assert.Equal("SecretValue", config[testContext.KeyVaultReferenceKey]);
        }

        /// <summary>
        /// Tests that Key Vault secrets are properly cached to avoid unnecessary requests.
        /// </summary>
        [Fact]
        public async Task KeyVaultReference_UsesCache_DoesNotCallKeyVaultAgain()
        {
            // Arrange - Setup test-specific keys
            TestContext testContext = CreateTestContext("KeyVaultCacheTest");
            await SetupKeyVaultReferences(testContext);

            // Create a monitoring client to track calls to Key Vault
            int requestCount = 0;
            var testSecretClient = new SecretClient(
                _keyVaultEndpoint,
                _defaultAzureCredential,
                new SecretClientOptions
                {
                    Transport = new HttpPipelineTransportWithRequestCount(() => requestCount++)
                });

            // Act
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{testContext.KeyPrefix}:*", KeyVaultReferenceLabel);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(testSecretClient);
                    });
                })
                .Build();

            // First access should resolve from Key Vault
            string firstValue = config[testContext.KeyVaultReferenceKey];
            int firstRequestCount = requestCount;

            // Second access should use the cache
            string secondValue = config[testContext.KeyVaultReferenceKey];
            int secondRequestCount = requestCount;

            // Assert
            Assert.Equal(testContext.SecretValue, firstValue);
            Assert.Equal(testContext.SecretValue, secondValue);
            Assert.Equal(1, firstRequestCount);  // Should make exactly one request
            Assert.Equal(firstRequestCount, secondRequestCount); // No additional requests for the second access
        }

        [Fact]
        public async Task KeyVaultReference_DifferentRefreshIntervals()
        {
            // Arrange - Setup test-specific keys
            TestContext testContext = CreateTestContext("KeyVaultDifferentIntervals");
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
                    options.Connect(_connectionString);
                    options.Select($"{testContext.KeyPrefix}:*", KeyVaultReferenceLabel);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetCredential(_defaultAzureCredential);
                        // Set different refresh intervals for each secret
                        kv.SetSecretRefreshInterval(kvRefKey1, TimeSpan.FromSeconds(60)); // Short interval
                        kv.SetSecretRefreshInterval(kvRefKey2, TimeSpan.FromDays(1));    // Long interval
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

        [Fact]
        public async Task SnapshotReference_ResolveCorrectly()
        {
            TestContext testContext = CreateTestContext("SnapshotReference");
            await SetupKeyValues(testContext);
            await SetUpSnapshotReferences(testContext);

            await _configClient.SetConfigurationSettingAsync(
                new ConfigurationSetting($"{testContext.KeyPrefix}:Setting1", "UpdatedAfterSnapshot"));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{testContext.KeyPrefix}:Setting*");
                    options.Select(testContext.SnapshotReferenceKey);
                })
                .Build();

            // Assert - Should get values from snapshot, not current values
            Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]);

            await _configClient.ArchiveSnapshotAsync(testContext.SnapshotName);
        }

        [Fact]
        public async Task SnapshotReference_HandleNonExistentSnapshot()
        {
            TestContext testContext = CreateTestContext("SnapshotRefNonExistent");
            await SetupKeyValues(testContext);

            string nonExistentSnapshotName = $"snapshot-does-not-exist-{testContext.SnapshotName}";

            // Create snapshot reference pointing to non-existent snapshot
            ConfigurationSetting snapshotReferenceSetting = ConfigurationModelFactory.ConfigurationSetting(
                testContext.SnapshotReferenceKey,
                JsonSerializer.Serialize(new { snapshot_name = nonExistentSnapshotName }),
                contentType: SnapshotReferenceConstants.ContentType
            );
            await _configClient.SetConfigurationSettingAsync(snapshotReferenceSetting);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{testContext.KeyPrefix}:*");
                })
                .Build();

            // Assert - Empty result for snapshot reference
            // Live values should still be accessible
            Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
            Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]);

            Assert.Null(config[testContext.SnapshotReferenceKey]);
        }

        [Fact]
        public async Task SnapshotReference_WithKeyVaultReference()
        {
            TestContext testContext = CreateTestContext("SnapshotRefKeyVault");
            await SetupKeyValues(testContext);

            // Create a Key Vault reference WITHOUT label
            string keyVaultReferenceKeyNoLabel = $"{testContext.KeyPrefix}:KeyVaultRefNoLabel";

            if (_secretClient != null)
            {
                await _secretClient.SetSecretAsync(testContext.KeyPrefix + "-secret", testContext.SecretValue);

                string keyVaultUri = $"{_keyVaultEndpoint}secrets/{testContext.KeyPrefix}-secret";
                string keyVaultRefValue = @$"{{""uri"":""{keyVaultUri}""}}";

                ConfigurationSetting keyVaultRefSetting = ConfigurationModelFactory.ConfigurationSetting(
                    keyVaultReferenceKeyNoLabel,
                    keyVaultRefValue,
                    contentType: KeyVaultConstants.ContentType);

                await _configClient.SetConfigurationSettingAsync(keyVaultRefSetting);
            }

            var settingsToInclude = new List<ConfigurationSettingsFilter>
            {
                new ConfigurationSettingsFilter(testContext.KeyPrefix + "*")
            };
            await CreateSnapshot(testContext.SnapshotName, settingsToInclude, SnapshotComposition.Key);

            ConfigurationSetting snapshotReferenceSetting = ConfigurationModelFactory.ConfigurationSetting(
                testContext.SnapshotReferenceKey,
                JsonSerializer.Serialize(new { snapshot_name = testContext.SnapshotName }),
                contentType: SnapshotReferenceConstants.ContentType
            );
            await _configClient.SetConfigurationSettingAsync(snapshotReferenceSetting);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select(testContext.SnapshotReferenceKey);
                    options.ConfigureKeyVault(kv => kv.SetCredential(_defaultAzureCredential));
                })
                .Build();

            try
            {
                Assert.Equal("InitialValue1", config[$"{testContext.KeyPrefix}:Setting1"]);
                Assert.Equal("InitialValue2", config[$"{testContext.KeyPrefix}:Setting2"]);
                Assert.Equal("SecretValue", config[keyVaultReferenceKeyNoLabel]);
            }
            finally
            {
                await _configClient.ArchiveSnapshotAsync(testContext.SnapshotName);
            }
        }

        [Fact]
        public async Task RequestTracing_SetsCorrectCorrelationContextHeader()
        {
            // Arrange - Setup test-specific keys
            TestContext testContext = CreateTestContext("RequestTracing");
            await SetupFeatureFlags(testContext);
            await SetupKeyVaultReferences(testContext);

            // Used to trigger FMVer tag in request tracing
            IFeatureManager featureManager;

            // Create a custom HttpPipeline that can inspect outgoing requests
            var requestInspector = new RequestInspectionHandler();

            await _configClient.SetConfigurationSettingAsync(
                ConfigurationModelFactory.ConfigurationSetting(
                    testContext.FeatureFlagKey,
                    @"{
                        ""id"": """ + testContext.KeyPrefix + @"Feature"",
                        ""description"": ""Test feature with filters"",
                        ""enabled"": true,
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
                    contentType: FeatureFlagContentType));

            IConfigurationRefresher refresher = null;

            using HttpClientTransportWithRequestInspection transportWithRequestInspection = new HttpClientTransportWithRequestInspection(requestInspector);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{testContext.KeyPrefix}:*");
                    options.ConfigureClientOptions(clientOptions =>
                    {
                        clientOptions.Transport = transportWithRequestInspection;
                    });
                    options.ConfigureKeyVault(kv => kv.SetCredential(_defaultAzureCredential));
                    options.UseFeatureFlags();
                    options.LoadBalancingEnabled = true;
                    refresher = options.GetRefresher();
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.RegisterAll();
                        refresh.SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                })
                .Build();

            // Assert - Verify correlation context headers

            // Basic request should have at least the request type
            Assert.Contains(RequestTracingConstants.RequestTypeKey, requestInspector.CorrelationContextHeaders.Last());
            Assert.Contains("Startup", requestInspector.CorrelationContextHeaders.Last());
            Assert.Contains(RequestTracingConstants.KeyVaultConfiguredTag, requestInspector.CorrelationContextHeaders.Last());
            Assert.Contains(RequestTracingConstants.LoadBalancingEnabledTag, requestInspector.CorrelationContextHeaders.Last());

            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting($"{testContext.KeyPrefix}:Setting1", "UpdatedValue1"));
            await Task.Delay(1500);
            await refresher.RefreshAsync();

            Assert.Contains("Watch", requestInspector.CorrelationContextHeaders.Last());
            Assert.Contains(RequestTracingConstants.FeatureFlagFilterTypeKey, requestInspector.CorrelationContextHeaders.Last());
            Assert.Contains(RequestTracingConstants.TimeWindowFilter, requestInspector.CorrelationContextHeaders.Last());
            Assert.Contains(RequestTracingConstants.CustomFilter, requestInspector.CorrelationContextHeaders.Last());
            Assert.Contains(RequestTracingConstants.FeatureFlagMaxVariantsKey, requestInspector.CorrelationContextHeaders.Last());
            Assert.Contains($"{RequestTracingConstants.FeatureManagementVersionKey}=4.3.0", requestInspector.CorrelationContextHeaders.Last());
        }

        [Fact]
        public async Task TagFilters()
        {
            TestContext testContext = CreateTestContext("TagFilters");
            await SetupTaggedSettings(testContext);
            string keyPrefix = testContext.KeyPrefix;

            // Test case 1: Basic tag filtering with single tag
            var config1 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{keyPrefix}:*", tagFilters: new[] { "Environment=Development" });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select($"{keyPrefix}:*", tagFilters: new[] { "Environment=Development" });
                    });
                })
                .Build();

            // Assert - Should only get settings with Environment=Development tag
            Assert.Equal("Value1", config1[$"{keyPrefix}:TaggedSetting1"]);
            Assert.Equal("Value3", config1[$"{keyPrefix}:TaggedSetting3"]);
            Assert.Null(config1[$"{keyPrefix}:TaggedSetting2"]);
            Assert.Null(config1[$"{keyPrefix}:TaggedSetting4"]);
            Assert.Null(config1[$"{keyPrefix}:TaggedSetting5"]);

            // Feature flags should be filtered as well
            Assert.Equal("True", config1[$"FeatureManagement:{keyPrefix}:FeatureDev"]);
            Assert.Null(config1[$"FeatureManagement:{keyPrefix}:FeatureProd"]);
            Assert.Null(config1[$"FeatureManagement:{keyPrefix}:FeatureSpecial"]);
            Assert.Null(config1[$"FeatureManagement:{keyPrefix}:FeatureEmpty"]);

            // Test case 2: Multiple tag filters (AND condition)
            var config2 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{keyPrefix}:*", tagFilters: new[] { "Environment=Development", "App=TestApp" });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select($"{keyPrefix}:*", tagFilters: new[] { "Environment=Development", "App=TestApp" });
                    });
                })
                .Build();

            // Assert - Should only get settings with both Environment=Development AND App=TestApp tags
            Assert.Equal("Value1", config2[$"{keyPrefix}:TaggedSetting1"]);
            Assert.Null(config2[$"{keyPrefix}:TaggedSetting2"]);
            Assert.Null(config2[$"{keyPrefix}:TaggedSetting3"]);
            Assert.Null(config2[$"{keyPrefix}:TaggedSetting4"]);
            Assert.Null(config2[$"{keyPrefix}:TaggedSetting5"]);

            // Feature flags
            Assert.Equal("True", config2[$"FeatureManagement:{keyPrefix}:FeatureDev"]);
            Assert.Null(config2[$"FeatureManagement:{keyPrefix}:FeatureProd"]);
            Assert.Null(config2[$"FeatureManagement:{keyPrefix}:FeatureSpecial"]);
            Assert.Null(config2[$"FeatureManagement:{keyPrefix}:FeatureEmpty"]);

            // Test case 3: Special characters in tags
            var config3 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{keyPrefix}:*", tagFilters: new[] { "Special:Tag=Value:With:Colons" });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select($"{keyPrefix}:*", tagFilters: new[] { "Special:Tag=Value:With:Colons" });
                    });
                })
                .Build();

            // Assert - Should only get settings with the special character tag
            Assert.Equal("Value4", config3[$"{keyPrefix}:TaggedSetting4"]);
            Assert.Null(config3[$"{keyPrefix}:TaggedSetting1"]);

            Assert.Null(config3[$"{keyPrefix}:TaggedSetting2"]);
            Assert.Null(config3[$"{keyPrefix}:TaggedSetting3"]);
            Assert.Null(config3[$"{keyPrefix}:TaggedSetting5"]);

            // Feature flags
            Assert.Equal("True", config3[$"FeatureManagement:{keyPrefix}:FeatureSpecial"]);
            Assert.Null(config3[$"FeatureManagement:{keyPrefix}:FeatureDev"]);
            Assert.Null(config3[$"FeatureManagement:{keyPrefix}:FeatureProd"]);
            Assert.Null(config3[$"FeatureManagement:{keyPrefix}:FeatureEmpty"]);

            // Test case 4: Tag with @ symbol
            var config4 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{keyPrefix}:*", tagFilters: new[] { "Tag@With@At=Value@With@At" });
                })
                .Build();

            // Assert - Should only get settings with the @ symbol tag
            Assert.Equal("Value4", config4[$"{keyPrefix}:TaggedSetting4"]);
            Assert.Null(config4[$"{keyPrefix}:TaggedSetting1"]);
            Assert.Null(config4[$"{keyPrefix}:TaggedSetting2"]);
            Assert.Null(config4[$"{keyPrefix}:TaggedSetting3"]);
            Assert.Null(config4[$"{keyPrefix}:TaggedSetting5"]);

            // Test case 5: Empty and null tag values
            var config5 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{keyPrefix}:*", tagFilters: new[] { "EmptyTag=", $"NullTag={TagValue.Null}" });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select($"{keyPrefix}:*", tagFilters: new[] { "EmptyTag=", $"NullTag={TagValue.Null}" });
                    });
                })
                .Build();

            // Assert - Should only get settings with both empty and null tag values
            Assert.Equal("Value5", config5[$"{keyPrefix}:TaggedSetting5"]);
            Assert.Null(config5[$"{keyPrefix}:TaggedSetting1"]);
            Assert.Null(config5[$"{keyPrefix}:TaggedSetting2"]);
            Assert.Null(config5[$"{keyPrefix}:TaggedSetting3"]);
            Assert.Null(config5[$"{keyPrefix}:TaggedSetting4"]);

            // Feature flags
            Assert.Equal("False", config5[$"FeatureManagement:{keyPrefix}:FeatureEmpty"]);
            Assert.Null(config5[$"FeatureManagement:{keyPrefix}:FeatureDev"]);
            Assert.Null(config5[$"FeatureManagement:{keyPrefix}:FeatureProd"]);
            Assert.Null(config5[$"FeatureManagement:{keyPrefix}:FeatureSpecial"]);

            // Test case 6: Interaction with refresh functionality
            IConfigurationRefresher refresher = null;
            var config9 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(_connectionString);
                    options.Select($"{keyPrefix}:*", tagFilters: new[] { "Environment=Development" });
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey, refreshAll: true)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            // Assert initial state
            Assert.Equal("Value1", config9[$"{keyPrefix}:TaggedSetting1"]);
            Assert.Equal("Value3", config9[$"{keyPrefix}:TaggedSetting3"]);

            // Update a tagged setting's value
            await _configClient.SetConfigurationSettingAsync(
                CreateSettingWithTags(
                    $"{keyPrefix}:TaggedSetting1",
                    "UpdatedValue1",
                    new Dictionary<string, string> {
                        { "Environment", "Development" },
                        { "App", "TestApp" }
                    }));

            // Add a new setting with Development tag
            await _configClient.SetConfigurationSettingAsync(
                CreateSettingWithTags(
                    $"{keyPrefix}:TaggedSetting7",
                    "Value7",
                    new Dictionary<string, string> {
                        { "Environment", "Development" }
                    }));

            // Update the sentinel key to trigger refresh
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(testContext.SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Refresh the configuration
            await refresher.RefreshAsync();

            Assert.Equal("UpdatedValue1", config9[$"{keyPrefix}:TaggedSetting1"]);
            Assert.Equal("Value3", config9[$"{keyPrefix}:TaggedSetting3"]);
            Assert.Equal("Value7", config9[$"{keyPrefix}:TaggedSetting7"]);
            Assert.Null(config9[$"{keyPrefix}:TaggedSetting2"]);
        }

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

        private class HttpClientTransportWithRequestInspection : HttpClientTransport
        {
            private readonly RequestInspectionHandler _inspector;

            public HttpClientTransportWithRequestInspection(RequestInspectionHandler inspector)
            {
                _inspector = inspector;
            }

            public override async ValueTask ProcessAsync(HttpMessage message)
            {
                _inspector.InspectRequest(message);
                await base.ProcessAsync(message);
            }
        }

        private class RequestInspectionHandler
        {
            public List<string> CorrelationContextHeaders { get; } = new List<string>();

            public void InspectRequest(HttpMessage message)
            {
                if (message.Request.Headers.TryGetValue(RequestTracingConstants.CorrelationContextHeader, out string header))
                {
                    CorrelationContextHeaders.Add(header);
                }
            }
        }

        private ConfigurationSetting CreateSettingWithTags(string key, string value, IDictionary<string, string> tags)
        {
            var setting = new ConfigurationSetting(key, value);

            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    setting.Tags.Add(tag.Key, tag.Value);
                }
            }

            return setting;
        }

        private ConfigurationSetting CreateFeatureFlagWithTags(string featureId, bool enabled, IDictionary<string, string> tags)
        {
            string jsonValue = $@"{{
                ""id"": ""{featureId}"",
                ""description"": ""Test feature flag with tags"",
                ""enabled"": {enabled.ToString().ToLowerInvariant()},
                ""conditions"": {{
                    ""client_filters"": []
                }}
            }}";

            var setting = new ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + featureId,
                value: jsonValue)
            {
                ContentType = FeatureFlagContentType
            };

            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    setting.Tags.Add(tag.Key, tag.Value);
                }
            }

            return setting;
        }
    }
}
