// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
                key: "TestKey1",
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

            var mockClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = mockClientProvider;
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
            Assert.True(ValidateLoggedError(mockLogger, LoggingConstants.RefreshFailedError));
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
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
            Assert.True(ValidateLoggedError(mockLogger, LoggingConstants.RefreshFailedDueToAuthenticationError));
        }

        [Fact]
        public void ValidateKeyVaultExceptionLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan cacheExpirationTime = TimeSpan.FromSeconds(1);

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

            var mockClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
            // Mock ILogger and ILoggerFactory
            var mockLogger = new Mock<ILogger>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(mlf => mlf.CreateLogger(LoggingConstants.AppConfigRefreshLogCategory)).Returns(mockLogger.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = mockClientProvider;
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

            Assert.True(ValidateLoggedError(mockLogger, LoggingConstants.RefreshFailedDueToKeyVaultError));
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
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
            cancellationSource.CancelAfter(TimeSpan.Zero);
            refresher.TryRefreshAsync(cancellationSource.Token).Wait();

            Assert.NotEqual("newValue1", config["TestKey1"]);
            Assert.True(ValidateLoggedError(mockLogger, LoggingConstants.RefreshCanceledError));
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
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
            Assert.True(ValidateLoggedError(mockLogger2, LoggingConstants.RefreshFailedDueToAuthenticationError));
        }

        private bool ValidateLoggedError(Mock<ILogger> logger, string expectedMessage)
        {
            Func<object, Type, bool> state = (v, t) => v.ToString().StartsWith(expectedMessage);

            logger.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Warning),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => state(v, t)),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)));

            return true;
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
