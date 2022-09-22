using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Azure.Data.AppConfiguration;
using Azure;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using System.Linq;
using Azure.Core.Testing;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;

namespace Tests.AzureAppConfiguration
{
    public class MapTests
    {
        List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey1",
                label: "label",
                value: "TestValue1",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey2",
                label: "label",
                value: "TestValue2",
                eTag: new ETag("31c38369-831f-4bf1-b9ad-79db56c8b989"),
                contentType: "text")
        };

        ConfigurationSetting FirstKeyValue => _kvCollection.First();
        ConfigurationSetting sentinelKv = new ConfigurationSetting("SentinelKey", "SentinelValue");
        ConfigurationSetting _kvr = ConfigurationModelFactory.ConfigurationSetting(
        key: "TestKey3",
        value: @"
                        {
                            ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                        }",
        eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
        contentType: KeyVaultConstants.ContentType + "; charset=utf-8");

        TimeSpan CacheExpirationTime = TimeSpan.FromSeconds(1);

        string _certValue = "Certificate Value from KeyVault";
        Uri vaultUri = new Uri("https://keyvault-theclassics.vault.azure.net");

        [Fact]
        public void MapTransformKeyValue()
        {
            var mockClient = GetMockConfigurationClient();

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.Map((setting) =>
                    {
                        if (setting.Key == "TestKey1")
                        {
                            setting.Value += " mapped";
                        }
                        return new ValueTask<ConfigurationSetting>(setting);
                    }).Map((setting) =>
                    {
                        if (setting.Value.EndsWith("mapped"))
                        {
                            setting.Value += " first";
                        }
                        else
                        {
                            setting.Value += " second";
                        }
                        return new ValueTask<ConfigurationSetting>(setting);
                    });
                })
                .Build();

            Assert.Equal("TestValue1 mapped first", config["TestKey1"]);
            Assert.Equal("TestValue2 second", config["TestKey2"]);
        }

        [Fact]
        public void MapTransformKeyVaultValueBeforeAdapters()
        {
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kvr }));

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(vaultUri);
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _certValue))));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                    options.Map((setting) =>
                    {
                        if (setting.ContentType == KeyVaultConstants.ContentType + "; charset=utf-8")
                        {
                            setting.Value = @"
                                            {
                                                ""uri"":""https://keyvault-theclassics.vault.azure.net/certificates/TestCertificate""
                                            }";
                        }
                        return new ValueTask<ConfigurationSetting>(setting);
                    });
                })
                .Build();

            Assert.Equal(_certValue, config["TestKey3"]);
        }

        [Fact]
        public void MapTransformWithRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", true)
                            .SetCacheExpiration(CacheExpirationTime);
                    });
                    options.Map((setting) =>
                    {
                        if (setting.Key == "TestKey1")
                        {
                            setting.Value += " mapped";
                        }
                        return new ValueTask<ConfigurationSetting>(setting);
                    }).Map((setting) =>
                    {
                        if (setting.Value.EndsWith("mapped"))
                        {
                            setting.Value += " first";
                        }
                        else
                        {
                            setting.Value += " second";
                        }
                        return new ValueTask<ConfigurationSetting>(setting);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1 mapped first", config["TestKey1"]);
            Assert.Equal("TestValue2 second", config["TestKey2"]);

            FirstKeyValue.Value = "newValue1";

            Thread.Sleep(CacheExpirationTime);
            refresher.TryRefreshAsync().Wait();

            Assert.Equal("newValue1 mapped first", config["TestKey1"]);
            Assert.Equal("TestValue2 second", config["TestKey2"]);
        }

        [Fact]
        public void MapTransformFeatureFlagWithRefresh()
        {
            ConfigurationSetting _kv = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "myFeature",
            value: @"
                                {
                                    ""id"": ""MyFeature"",
                                    ""description"": ""The new beta version of our web site."",
                                    ""display_name"": ""Beta Feature"",
                                    ""enabled"": true,
                                    ""conditions"": {
                                    ""client_filters"": [
                                        {
                                        ""name"": ""AllUsers""
                                        }, 
                                        {
                                        ""name"": ""SuperUsers""
                                        }
                                    ]
                                    }
                                }
                                ",
            label: default,
            contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"));

            IConfigurationRefresher refresher = null;
            var featureFlags = new List<ConfigurationSetting> { _kv };
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", true)
                            .SetCacheExpiration(CacheExpirationTime);
                    });
                    options.UseFeatureFlags(o => o.CacheExpirationInterval = CacheExpirationTime);
                    options.Map((setting) =>
                    {
                        if (setting.ContentType == FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8")
                        {
                            setting.Value = @"
                                {
                                    ""id"": ""MyFeature"",
                                    ""description"": ""The new beta version of our web site."",
                                    ""display_name"": ""Beta Feature"",
                                    ""enabled"": true,
                                    ""conditions"": {
                                    ""client_filters"": [
                                        {
                                        ""name"": ""NoUsers""
                                        }, 
                                        {
                                        ""name"": ""SuperUsers""
                                        }
                                    ]
                                    }
                                }
                                ";
                        }
                        return new ValueTask<ConfigurationSetting>(setting);
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("NoUsers", config["FeatureManagement:MyFeature:EnabledFor:0:Name"]);

            FirstKeyValue.Value = "newValue1";
            featureFlags[0] = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "myFeature",
            value: @"
                                {
                                  ""id"": ""MyFeature"",
                                  ""description"": ""The new beta version of our web site."",
                                  ""display_name"": ""Beta Feature"",
                                  ""enabled"": true,
                                  ""conditions"": {
                                    ""client_filters"": [                        
                                      {
                                        ""name"": ""SuperUsers""
                                      }
                                    ]
                                  }
                                }
                                ",
            label: default,
            contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            Thread.Sleep(CacheExpirationTime);
            refresher.TryRefreshAsync().Wait();

            Assert.Equal("newValue1", config["TestKey1"]);
            Assert.Equal("NoUsers", config["FeatureManagement:MyFeature:EnabledFor:0:Name"]);
        }

        private Mock<ConfigurationClient> GetMockConfigurationClient()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return Response.FromValue(TestHelpers.CloneSetting(_kvCollection.FirstOrDefault(s => s.Key == key && s.Label == label)), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var newSetting = _kvCollection.FirstOrDefault(s => (s.Key == setting.Key && s.Label == setting.Label));
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            // We don't actually select KV based on SettingSelector, we just return a deep copy of _kvCollection
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    return new MockAsyncPageable(_kvCollection.Select(setting => TestHelpers.CloneSetting(setting)).ToList());
                });

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            return mockClient;
        }
    }
}
