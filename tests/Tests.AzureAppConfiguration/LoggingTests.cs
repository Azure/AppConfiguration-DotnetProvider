// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Diagnostics;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class LoggingTests
    {
        private readonly List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey1",
                label: "label",
                value: "TestValue1",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey2",
                label: null,
                value: "TestValue2",
                eTag: new ETag("31c38369-831f-4bf1-b9ad-79db56c8b989"),
                contentType: "text")
        };

        private ConfigurationSetting FirstKeyValue => _kvCollection.First();
        private readonly ConfigurationSetting sentinelKv = new ConfigurationSetting("SentinelKey", "SentinelValue");
        private readonly ConfigurationSetting _kvr = ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey3",
                label: "label3",
                value: @"
                        {
                            ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                        }",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8");
        private readonly TimeSpan CacheExpirationTime = TimeSpan.FromSeconds(1);

        [Fact]
        public async Task ValidateExceptionLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .Throws(new RequestFailedException("Request failed."));

            string warningInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Warning)
                    {
                        warningInvocation += s;
                    }
                }, EventLevel.Verbose);

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
            await refresher.TryRefreshAsync();

            Assert.NotEqual("newValue1", config["TestKey1"]);
            Assert.Contains(LoggingConstants.RefreshFailedError, warningInvocation);
        }

        [Fact]
        public async Task ValidateUnauthorizedExceptionLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .Throws(new RequestFailedException(401, "Unauthorized"));

            string warningInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Warning)
                    {
                        warningInvocation += s;
                    }
                }, EventLevel.Verbose);

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
            await refresher.TryRefreshAsync();

            Assert.NotEqual("newValue1", config["TestKey1"]);
            Assert.Contains(LoggingConstants.RefreshFailedDueToAuthenticationError, warningInvocation);
        }

        [Fact]
        public async Task ValidateInvalidOperationExceptionLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .Throws(new InvalidOperationException());

            string warningInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Warning)
                    {
                        warningInvocation += s;
                    }
                }, EventLevel.Verbose);

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
            await refresher.TryRefreshAsync();

            Assert.NotEqual("newValue1", config["TestKey1"]);
            Assert.Contains(LoggingConstants.RefreshFailedError, warningInvocation);
        }

        [Fact]
        public async Task ValidateKeyVaultExceptionLoggedDuringRefresh()
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

            string warningInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Warning)
                    {
                        warningInvocation += s;
                    }
                }, EventLevel.Verbose);

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
            await refresher.TryRefreshAsync();

            Assert.Contains(LoggingConstants.RefreshFailedDueToKeyVaultError + "\nNo key vault credential or secret resolver callback configured, and no matching secret client could be found.", warningInvocation);
        }

        [Fact]
        public async Task ValidateAggregateExceptionWithInnerOperationCanceledExceptionLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Throws(new AggregateException("Retry failed.", new List<Exception> { new OperationCanceledException(), new RequestFailedException("Request failed.") }));

            string warningInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Warning)
                    {
                        warningInvocation += s;
                    }
                }, EventLevel.Verbose);

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
            await refresher.TryRefreshAsync();

            Assert.NotEqual("newValue1", config["TestKey1"]);
            Assert.Contains(LoggingConstants.RefreshFailedError, warningInvocation);
        }

        [Fact]
        public async Task ValidateOperationCanceledExceptionLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            string warningInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Warning)
                    {
                        warningInvocation += s;
                    }
                }, EventLevel.Verbose);

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

            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            await refresher.TryRefreshAsync(cancellationSource.Token);

            Assert.NotEqual("newValue1", config["TestKey1"]);
            Assert.Contains(LoggingConstants.RefreshCanceledError, warningInvocation);
        }

        [Fact]
        public async Task ValidateFailoverToDifferentEndpointMessageLoggedAfterFailover()
        {
            IConfigurationRefresher refresher = null;
            var mockClient1 = GetMockConfigurationClient();
            var mockClient2 = GetMockConfigurationClient();

            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException(HttpStatusCodes.TooManyRequests, "Too many requests"));
            mockClient1.Setup(c => c.ToString()).Returns("client");
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);
            mockClient2.Setup(c => c.Equals(mockClient1)).Returns(true);

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, mockClient1.Object);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var clientList = new List<ConfigurationClientWrapper>() { cw1, cw2 };
            var configClientManager = new ConfigurationClientManager(clientList);

            string warningInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Warning)
                    {
                        warningInvocation += s;
                    }
                }, EventLevel.Verbose);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = configClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });

                    refresher = options.GetRefresher();

                    options.ReplicaDiscoveryEnabled = false;
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            Thread.Sleep(CacheExpirationTime);
            await refresher.TryRefreshAsync();

            Assert.Equal("newValue1", config["TestKey1"]);
            Assert.Contains(LogHelper.BuildFailoverMessage(TestHelpers.PrimaryConfigStoreEndpoint.ToString(), TestHelpers.SecondaryConfigStoreEndpoint.ToString()), warningInvocation);

            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException(HttpStatusCodes.TooManyRequests, "Too many requests"));
            mockClient2.Setup(c => c.ToString()).Returns("client");

            FirstKeyValue.Value = "TestValue1";

            Thread.Sleep(CacheExpirationTime);
            await refresher.TryRefreshAsync();

            Assert.Equal("newValue1", config["TestKey1"]);
            Assert.Contains(LogHelper.BuildLastEndpointFailedMessage(TestHelpers.SecondaryConfigStoreEndpoint.ToString()), warningInvocation);
        }

        [Fact]
        public async Task ValidateConfigurationUpdatedSuccessLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            string invocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Informational)
                    {
                        invocation += s;
                    }
                }, EventLevel.Verbose);

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
            await refresher.TryRefreshAsync();

            Assert.Equal("newValue1", config["TestKey1"]);
            Assert.Contains(LogHelper.BuildConfigurationUpdatedMessage(), invocation);
        }

        [Fact]
        public async Task ValidateCorrectEndpointLoggedOnConfigurationUpdate()
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

            string invocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Verbose)
                    {
                        invocation += s;
                    }
                }, EventLevel.Verbose);

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
            await refresher.TryRefreshAsync();

            // We should see the second client's endpoint logged since the first client is backed off
            Assert.Contains(LogHelper.BuildKeyValueReadMessage(KeyValueChangeType.Modified, _kvCollection[0].Key, _kvCollection[0].Label, TestHelpers.SecondaryConfigStoreEndpoint.ToString().TrimEnd('/')), invocation);
        }

        [Fact]
        public async Task ValidateCorrectKeyValueLoggedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            string informationalInvocation = "";
            string verboseInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Informational)
                    {
                        informationalInvocation += s;
                    }

                    if (args.Level == EventLevel.Verbose)
                    {
                        verboseInvocation += s;
                    }
                }, EventLevel.Verbose);

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", false).Register("TestKey2", false)
                            .SetCacheExpiration(CacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            Thread.Sleep(CacheExpirationTime);
            await refresher.TryRefreshAsync();

            Assert.Equal("newValue1", config["TestKey1"]);
            Assert.Contains(LogHelper.BuildKeyValueReadMessage(KeyValueChangeType.Modified, _kvCollection[0].Key, _kvCollection[0].Label, TestHelpers.PrimaryConfigStoreEndpoint.ToString().TrimEnd('/')), verboseInvocation);
            Assert.Contains(LogHelper.BuildKeyValueSettingUpdatedMessage(FirstKeyValue.Key), informationalInvocation);
            Assert.Contains(LogHelper.BuildKeyValueReadMessage(KeyValueChangeType.None, _kvCollection[1].Key, _kvCollection[1].Label, TestHelpers.PrimaryConfigStoreEndpoint.ToString().TrimEnd('/')), verboseInvocation);
        }

        [Fact]
        public async Task ValidateCorrectKeyVaultSecretLoggedDuringRefresh()
        {
            string _secretValue = "SecretValue from KeyVault";
            Uri vaultUri = new Uri("https://keyvault-theclassics.vault.azure.net");
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kvr }));

            string informationalInvocation = "";
            string verboseInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Informational)
                    {
                        informationalInvocation += s;
                    }

                    if (args.Level == EventLevel.Verbose)
                    {
                        verboseInvocation += s;
                    }
                }, EventLevel.Verbose);

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
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object).SetSecretRefreshInterval(CacheExpirationTime));
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
            await refresher.TryRefreshAsync();
            Assert.Contains(LogHelper.BuildKeyVaultSecretReadMessage(_kvr.Key, _kvr.Label), verboseInvocation);
            Assert.Contains(LogHelper.BuildKeyVaultSettingUpdatedMessage(_kvr.Key), informationalInvocation);
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

                var newSetting = _kvCollection.FirstOrDefault(s => s.Key == setting.Key && s.Label == setting.Label);
                var unchanged = newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value;
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
