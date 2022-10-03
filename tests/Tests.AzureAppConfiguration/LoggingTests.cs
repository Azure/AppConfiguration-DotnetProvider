// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class LoggingTests
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
                label: "label3",
                value: @"
                        {
                            ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                        }",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8");

        TimeSpan CacheExpirationTime = TimeSpan.FromSeconds(1);

        [Fact]
        public void ThrowsIfLoggerFactorySetWithIConfigurationRefresherBeforeBuild()
        {
            IConfigurationRefresher refresher = null;

            void action() => new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                    refresher.LoggerFactory = NullLoggerFactory.Instance; // Throws
                })
                .Build();

            Assert.Throws<ArgumentException>(action);
        }

        [Fact]
        public void ValidateExceptionLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .Throws(new RequestFailedException("Request failed."));

            var mockLogger = new Mock<ILogger>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger.Object);

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            Thread.Sleep(CacheExpirationTime);
            refresher.LoggerFactory = mockLoggerFactory.Object;
            refresher.TryRefreshAsync().Wait();

            Assert.NotEqual("newValue1", config["TestKey1"]);
            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshFailedError, LogLevel.Warning));
        }

        [Fact]
        public void ValidateUnauthorizedExceptionLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .Throws(new RequestFailedException(401, "Unauthorized"));

            var mockLogger = new Mock<ILogger>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";
            
            Thread.Sleep(CacheExpirationTime);
            refresher.LoggerFactory = mockLoggerFactory.Object;
            refresher.TryRefreshAsync().Wait();

            Assert.NotEqual("newValue1", config["TestKey1"]);
            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshFailedDueToAuthenticationError, LogLevel.Warning));
        }

        [Fact]
        public void ValidateKeyVaultExceptionLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;

            // Mock ConfigurationClient
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                return Response.FromValue(TestHelpers.CloneSetting(sentinelKv), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var unchanged = sentinelKv.Key == setting.Key && sentinelKv.Label == setting.Label && sentinelKv.Value == setting.Value;
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(sentinelKv, response);
            }

            // No KVR during startup; return KVR during refresh operation to see error because ConfigureKeyVault is missing
            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.Select(setting => TestHelpers.CloneSetting(setting)).ToList()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kvr }));
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
            // Mock ILogger and ILoggerFactory
            var mockLogger = new Mock<ILogger>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("SentinelKey", refreshAll: true)
                                      .SetCacheExpiration(CacheExpirationTime);
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("SentinelValue", config["SentinelKey"]);

            // Update sentinel key-value to trigger refreshAll operation
            sentinelKv.Value = "UpdatedSentinelValue";
            Thread.Sleep(CacheExpirationTime);
            refresher.LoggerFactory = mockLoggerFactory.Object;
            refresher.TryRefreshAsync().Wait();

            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshFailedDueToKeyVaultError, LogLevel.Warning));
        }

        [Fact]
        public void ValidateOperationCanceledExceptionLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var mockLogger = new Mock<ILogger>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            Thread.Sleep(CacheExpirationTime);
            refresher.LoggerFactory = mockLoggerFactory.Object;

            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            refresher.TryRefreshAsync(cancellationSource.Token).Wait();

            Assert.NotEqual("newValue1", config["TestKey1"]);
            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshCanceledError, LogLevel.Warning));
        }

        [Fact]
        public void ValidateConfigurationUpdatedSuccessLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var mockLogger = new Mock<ILogger>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger.Object);

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

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            Thread.Sleep(CacheExpirationTime);
            refresher.LoggerFactory = mockLoggerFactory.Object;
            refresher.TryRefreshAsync().Wait();

            Assert.Equal("newValue1", config["TestKey1"]);
            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshConfigurationUpdatedSuccess, LogLevel.Information));
        }

        [Fact]
        public void ValidateCorrectEndpointLoggedOnConfigurationUpdate()
        {
            IConfigurationRefresher refresher = null;
            var mockClient1 = new Mock<ConfigurationClient>();
            mockClient1.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
           .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);
            var mockClient2 = GetMockConfigurationClient();

            var mockLogger = new Mock<ILogger>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger.Object);

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient1.Object, mockClient2.Object);

            var config1 = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", true)
                            .SetCacheExpiration(CacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            FirstKeyValue.Value = "newValue1";

            Thread.Sleep(CacheExpirationTime);
            refresher.LoggerFactory = mockLoggerFactory.Object;
            refresher.TryRefreshAsync().Wait();

            // We should see the second client's endpoint logged since the first client is backed off
            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshConfigurationUpdatedSuccess + TestHelpers.SecondaryConfigStoreEndpoint.ToString(), LogLevel.Information));
        }

        [Fact]
        public void ValidateCorrectKeyNameLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var mockLogger = new Mock<ILogger>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger.Object);

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", false).Register("TestKey2", "label", false)
                            .SetCacheExpiration(CacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            Thread.Sleep(CacheExpirationTime);
            refresher.LoggerFactory = mockLoggerFactory.Object;
            refresher.TryRefreshAsync().Wait();

            Assert.Equal("newValue1", config["TestKey1"]);
            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshKeyValueChanged + "(key: 'TestKey1', label: 'label')", LogLevel.Debug));
            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshKeyValueSettingUpdated + "'TestKey1'", LogLevel.Information));
            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshKeyValueUnchanged + "(key: 'TestKey2', label: 'label')", LogLevel.Debug));
        }

        [Fact]
        public void ValidateCorrectKeyVaultSecretLoggedDuringRefresh()
        {
            string _secretValue = "SecretValue from KeyVault";
            Uri vaultUri = new Uri("https://keyvault-theclassics.vault.azure.net");
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kvr }));

            var mockLogger = new Mock<ILogger>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger.Object);

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(vaultUri);
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _secretValue))));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", true)
                                      .SetCacheExpiration(CacheExpirationTime);
                    });
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object).SetSecretRefreshInterval(TimeSpan.FromSeconds(1)));
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("SecretValue from KeyVault", config[_kvr.Key]);

            _kvr.Value = @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/Password3/6db5a48680104dda9097b1e6d859e553""
                    }
                   ";
            Thread.Sleep(CacheExpirationTime);
            refresher.LoggerFactory = mockLoggerFactory.Object;
            refresher.TryRefreshAsync().Wait();
            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshKeyVaultSecretChanged + "(key: 'TestKey3', label: 'label3')", LogLevel.Debug));
            Assert.True(TestHelpers.ValidateLog(mockLogger, LoggingConstants.RefreshKeyVaultSettingUpdated + "'TestKey3'", LogLevel.Information));
        }

        [Fact]
        public void OverwriteLoggerFactory()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .Throws(new RequestFailedException(403, "Forbidden"));

            var mockLogger1 = new Mock<ILogger>();
            var mockLoggerFactory1 = new Mock<ILoggerFactory>();
            mockLoggerFactory1.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger1.Object);

            var mockLogger2 = new Mock<ILogger>();
            var mockLoggerFactory2 = new Mock<ILoggerFactory>();
            mockLoggerFactory2.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger2.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";
            
            // Set LoggerFactory
            refresher.LoggerFactory = mockLoggerFactory1.Object;

            // Overwrite LoggerFactory
            refresher.LoggerFactory = mockLoggerFactory2.Object;

            Thread.Sleep(CacheExpirationTime);
            refresher.TryRefreshAsync().Wait();

            Assert.NotEqual("newValue1", config["TestKey1"]);
            Assert.True(TestHelpers.ValidateLog(mockLogger2, LoggingConstants.RefreshFailedDueToAuthenticationError, LogLevel.Warning));
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
