using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppConfiguration;
using Azure.ResourceManager.AppConfiguration.Models;
using Azure.ResourceManager.KeyVault;
using Azure.ResourceManager.KeyVault.Models;
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
        private const string KeyVaultNamePrefix = "kv-int-";
        private const string TestResourceTag = "TestResource";
        private const string CreatedByTag = "CreatedBy";
        private const int StaleResourceThresholdHours = 3; // Resources older than this are considered stale

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
        }

        // Client for direct manipulation of the store
        private ConfigurationClient _configClient;

        // Client for Key Vault operations
        private SecretClient _secretClient;

        // Connection string for the store
        private string _connectionString;

        // Store management resources
        private ArmClient _armClient;
        private string _testStoreName;
        private string _testKeyVaultName;
        private string _testResourceGroupName;
        private AppConfigurationStoreResource _appConfigStore;
        private KeyVaultResource _keyVault;
        private Uri _appConfigEndpoint;
        private Uri _keyVaultEndpoint;
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

                return operation.Value.Name;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating snapshot: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Cleans up only stale App Configuration stores and Key Vaults, not resource groups
        /// </summary>
        private async Task CleanupStaleResources()
        {
            if (_resourceGroup == null) return;

            try
            {
                // Clean up stale App Configuration stores
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

                // Clean up stale Key Vaults
                var keyVaults = _resourceGroup.GetKeyVaults();

                await foreach (var vault in keyVaults.GetAllAsync())
                {
                    // Only delete key vaults that:
                    // 1. Start with our test prefix
                    // 2. Have the TestResourceTag
                    // 3. Are older than the threshold
                    if (!vault.Data.Name.StartsWith(KeyVaultNamePrefix) ||
                        !vault.Data.Tags.ContainsKey(TestResourceTag))
                    {
                        continue;
                    }

                    // Check if the key vault is a temporary test resource
                    if (vault.Data.Tags.ContainsKey("TemporaryStore") &&
                        vault.Data.Tags["TemporaryStore"] == "true")
                    {
                        // If it has a creation time tag, check if it's stale
                        if (vault.Data.Tags.TryGetValue("CreatedOn", out string createdOnStr) &&
                            DateTime.TryParse(createdOnStr, out DateTime createdOn))
                        {
                            if (createdOn < staleTime)
                            {
                                await vault.DeleteAsync(WaitUntil.Started);
                            }
                        }
                        else
                        {
                            // If no creation time or it can't be parsed, use a heuristic
                            // based on the timestamp in the name
                            string name = vault.Data.Name;
                            if (name.Length > KeyVaultNamePrefix.Length + 12) // yyyyMMddHHmm format is 12 chars
                            {
                                string timeStampPart = name.Substring(KeyVaultNamePrefix.Length, 12);
                                if (DateTime.TryParseExact(timeStampPart, "yyyyMMddHHmm",
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.None, out DateTime timestamp))
                                {
                                    if (timestamp < staleTime)
                                    {
                                        await vault.DeleteAsync(WaitUntil.Started);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during stale resource cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a temporary Azure App Configuration store and Key Vault, then adds test data.
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
                await CleanupStaleResources();

                // Create unique store name for this test run with timestamp
                _testStoreName = GenerateTimestampedResourceName(StoreNamePrefix);
                _testKeyVaultName = GenerateTimestampedResourceName(KeyVaultNamePrefix);

                // Create the App Configuration store
                var storeData = new AppConfigurationStoreData(new AzureLocation(DefaultLocation), new AppConfigurationSku("free"));

                storeData.Tags.Add(TestResourceTag, "true");
                storeData.Tags.Add(CreatedByTag, "IntegrationTests");
                storeData.Tags.Add("TemporaryStore", "true");
                storeData.Tags.Add("CreatedOn", DateTime.UtcNow.ToString("o"));

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

                // Create the Key Vault
                try
                {
                    // Create an access policy for the current user
                    var userObjectId = await GetCurrentUserObjectId(credential);

                    // Define vault properties
                    var vaultProperties = new KeyVaultProperties(
                        new Guid(await GetTenantId(credential)),
                        new KeyVaultSku(KeyVaultSkuFamily.A, KeyVaultSkuName.Standard));

                    //vaultProperties.AccessPolicies = {
                    //    new KeyVaultAccessPolicy
                    //    {
                    //        ObjectId = userObjectId,
                    //        TenantId = new Guid(await GetTenantId(credential)),
                    //        Permissions = new KeyVaultPermissions
                    //        {
                    //            Secrets = {
                    //                KeyVaultSecretPermission.Get,
                    //                KeyVaultSecretPermission.List,
                    //                KeyVaultSecretPermission.Set,
                    //                KeyVaultSecretPermission.Delete
                    //            }
                    //        }
                    //    }
                    //},

                    // Create Key Vault resource data
                    var vaultData = new KeyVaultCreateOrUpdateContent(new AzureLocation(DefaultLocation), vaultProperties);

                    // Add tags
                    vaultData.Tags.Add(TestResourceTag, "true");
                    vaultData.Tags.Add(CreatedByTag, "IntegrationTests");
                    vaultData.Tags.Add("TemporaryStore", "true");
                    vaultData.Tags.Add("CreatedOn", DateTime.UtcNow.ToString("o"));

                    // Create the vault
                    var vaultCreateOperation = await _resourceGroup.GetKeyVaults().CreateOrUpdateAsync(
                        WaitUntil.Completed,
                        _testKeyVaultName,
                        vaultData);

                    _keyVault = vaultCreateOperation.Value;
                    _keyVaultEndpoint = _keyVault.Data.Properties.VaultUri;

                    // Create a Secret Client for the vault
                    _secretClient = new SecretClient(_keyVaultEndpoint, credential);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating Key Vault: {ex.Message}");
                    // We'll continue without Key Vault if it fails
                }

                success = true;
            }
            finally
            {
                if (!success)
                {
                    await CleanupResources();
                }
            }
        }

        /// <summary>
        /// Get the current user's Object ID
        /// </summary>
        private async Task<Guid> GetCurrentUserObjectId(TokenCredential credential)
        {
            // Use the Microsoft Graph API to get the current user's object ID
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }),
                default);

            // Parse the token to get user information
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token.Token) as System.IdentityModel.Tokens.Jwt.JwtSecurityToken;

            // Read the object id (oid) claim
            string oid = jsonToken?.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;

            if (string.IsNullOrEmpty(oid))
            {
                throw new InvalidOperationException("Could not determine the current user's object ID.");
            }

            return new Guid(oid);
        }

        /// <summary>
        /// Get the tenant ID
        /// </summary>
        private async Task<string> GetTenantId(TokenCredential credential)
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://management.azure.com/.default" }),
                default);

            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token.Token) as System.IdentityModel.Tokens.Jwt.JwtSecurityToken;

            string tid = jsonToken?.Claims.FirstOrDefault(c => c.Type == "tid")?.Value;

            if (string.IsNullOrEmpty(tid))
            {
                throw new InvalidOperationException("Could not determine the tenant ID.");
            }

            return tid;
        }

        /// <summary>
        /// Deletes all created resources
        /// </summary>
        private async Task CleanupResources()
        {
            // First delete Key Vault
            if (_keyVault != null)
            {
                try
                {
                    Console.WriteLine($"Cleaning up test Key Vault: {_testKeyVaultName}");
                    await _keyVault.DeleteAsync(WaitUntil.Completed);
                    _keyVault = null;
                    Console.WriteLine("Key Vault cleanup completed successfully");
                }
                catch (Exception ex) when (
                    ex is RequestFailedException ||
                    ex is InvalidOperationException ||
                    ex is TaskCanceledException)
                {
                    Console.WriteLine($"Key Vault cleanup failed: {ex.Message}.");
                }
            }

            // Then delete App Configuration store
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
            await CleanupResources();

            try
            {
                await CleanupStaleResources();
            }
            catch (Exception ex) when (
                ex is RequestFailedException ||
                ex is InvalidOperationException ||
                ex is TaskCanceledException ||
                ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"Error during stale resource cleanup: {ex.Message}");
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
            string secretName = $"{keyPrefix}-secret";
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
            }

            // If Key Vault is available, add a test secret and reference
            if (_secretClient != null)
            {
                try
                {
                    // Create a secret in Key Vault
                    await _secretClient.SetSecretAsync(secretName, "SecretValue");

                    // Create a Key Vault reference in App Configuration
                    string keyVaultUri = $"{_keyVaultEndpoint}secrets/{secretName}";
                    string keyVaultRefValue = @$"{{""uri"":""{keyVaultUri}""}}";

                    await _configClient.SetConfigurationSettingAsync(
                        ConfigurationModelFactory.ConfigurationSetting(
                            keyVaultReferenceKey,
                            keyVaultRefValue,
                            contentType: KeyVaultConstants.ContentType));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error setting up Key Vault reference: {ex.Message}");
                    // Continue without Key Vault reference if it fails
                }
            }

            return new TestContext
            {
                KeyPrefix = keyPrefix,
                SentinelKey = sentinelKey,
                FeatureFlagKey = featureFlagKey,
                KeyVaultReferenceKey = keyVaultReferenceKey,
                SecretName = secretName
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
            // Skip if Key Vault is not available
            if (_keyVault == null || _secretClient == null)
            {
                Console.WriteLine("Skipping Key Vault test - Key Vault is not available");
                return;
            }

            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("KeyVaultReference");

            // Act - Create configuration with Key Vault support
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");
                    options.ConfigureKeyVault(kv => kv.SetCredential(GetCredential()));
                })
                .Build();

            // Assert - Key Vault reference should be resolved to the secret value
            Assert.Equal("SecretValue", config[testContext.KeyVaultReferenceKey]);
        }

        /// <summary>
        /// Test verifies Key Vault references refresh correctly
        /// </summary>
        [Fact]
        public async Task KeyVaultReferences_RefreshCorrectly()
        {
            // Skip if Key Vault is not available
            if (_keyVault == null || _secretClient == null)
            {
                Console.WriteLine("Skipping Key Vault test - Key Vault is not available");
                return;
            }

            // Arrange - Setup test-specific keys
            var testContext = await SetupTestKeys("KeyVaultRefresh");
            IConfigurationRefresher refresher = null;

            // Create configuration with Key Vault support and refresh
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(GetConnectionString());
                    options.Select($"{testContext.KeyPrefix}:*");
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetCredential(GetCredential());
                        kv.SetSecretRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register(testContext.SentinelKey, refreshAll: true)
                              .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Verify initial value
            Assert.Equal("SecretValue", config[testContext.KeyVaultReferenceKey]);

            // Act - Update the secret in Key Vault
            await _secretClient.SetSecretAsync(testContext.SecretName, "UpdatedSecretValue");

            // Update the sentinel key to trigger refresh
            await _configClient.SetConfigurationSettingAsync(new ConfigurationSetting(testContext.SentinelKey, "Updated"));

            // Wait for cache to expire
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Refresh
            await refresher.RefreshAsync();

            // Assert - Key Vault reference should be updated with the new secret value
            Assert.Equal("UpdatedSecretValue", config[testContext.KeyVaultReferenceKey]);
        }
    }
}
