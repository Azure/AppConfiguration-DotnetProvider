// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class RefreshTests
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
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey3",
                label: "label",
                value: "TestValue3",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKeyWithMultipleLabels",
                label: "label1",
                value: "TestValueForLabel1",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKeyWithMultipleLabels",
                label: "label2",
                value: "TestValueForLabel2",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text")
        };

        ConfigurationSetting FirstKeyValue => _kvCollection.First();

        [Fact]
        public void RefreshTests_RefreshRegisteredKeysAreLoadedOnStartup_DefaultUseQuery()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                return Response.FromValue(keyValueCollection.FirstOrDefault(s => s.Key == key && s.Label == label), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var newSetting = keyValueCollection.FirstOrDefault(s => (s.Key == setting.Key && s.Label == setting.Label));
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            // Load all settings except the one registered for refresh - this test is to ensure that it will be loaded later
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(keyValueCollection.Where(s => s.Key != "TestKey1" && s.Label != "label").ToList()));
            
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);
            
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register("TestKey1", "label")
                               .SetCacheExpiration(TimeSpan.FromSeconds(60));
                    });
                })
                .Build();
            
            Assert.Equal("TestValue1", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_RefreshRegisteredKeysAreLoadedOnStartup_CustomUseQuery()
        {
            var mockClient = GetMockConfigurationClientSelectKeyLabel();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey1", "label");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey2", "label")
                            .Register("TestKey3", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(60));
                    });
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
        }

        [Fact]
        public void RefreshTests_RefreshIsSkippedIfCacheIsNotExpired()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(10));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            refresher.RefreshAsync().Wait();

            Assert.Equal("TestValue1", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_RefreshIsSkippedIfKvNotInSelectAndCacheIsNotExpired()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClientSelectKeyLabel();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey2", "label");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(10));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            refresher.RefreshAsync().Wait();

            Assert.Equal("TestValue1", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_RefreshIsNotSkippedIfCacheIsExpired()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            // Wait for the cache to expire
            Thread.Sleep(1500);

            refresher.RefreshAsync().Wait();

            Assert.Equal("newValue", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_RefreshAllFalseDoesNotUpdateEntireConfiguration()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label") // refreshAll: false
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            keyValueCollection.ForEach(kv => kv.Value = "newValue");

            // Wait for the cache to expire
            Thread.Sleep(1500);

            refresher.RefreshAsync().Wait();

            Assert.Equal("newValue", config["TestKey1"]);
            Assert.NotEqual("newValue", config["TestKey2"]);
            Assert.NotEqual("newValue", config["TestKey3"]);
        }

        [Fact]
        public void RefreshTests_RefreshAllTrueUpdatesEntireConfiguration()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", refreshAll: true)
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            keyValueCollection.ForEach(kv => kv.Value = "newValue");

            // Wait for the cache to expire
            Thread.Sleep(1500);

            refresher.RefreshAsync().Wait();

            Assert.Equal("newValue", config["TestKey1"]);
            Assert.Equal("newValue", config["TestKey2"]);
            Assert.Equal("newValue", config["TestKey3"]);
        }

        [Fact]
        public void RefreshTests_RefreshAllTrueRemovesDeletedConfiguration()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetSettingFromService(string k, string l, CancellationToken ct)
            {
                return Response.FromValue(keyValueCollection.FirstOrDefault(s => s.Key == k && s.Label == l), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var newSetting = keyValueCollection.FirstOrDefault(s => s.Key == setting.Key && s.Label == setting.Label);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var copy = new List<ConfigurationSetting>();
                    foreach (var setting in keyValueCollection)
                    {
                        copy.Add(TestHelpers.CloneSetting(setting));
                    };

                    return new MockAsyncPageable(copy);
                });

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetSettingFromService);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", refreshAll: true)
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            keyValueCollection.First().Value = "newValue";
            keyValueCollection.Remove(keyValueCollection.FirstOrDefault(s => s.Key == "TestKey3" && s.Label == "label"));

            // Wait for the cache to expire
            Thread.Sleep(1500);

            refresher.RefreshAsync().Wait();

            Assert.Equal("newValue", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Null(config["TestKey3"]);
        }

        [Fact]
        public void RefreshTests_RefreshAllForNonExistentSentinelDoesNothing()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetSettingFromService(string k, string l, CancellationToken ct)
            {
                return Response.FromValue(keyValueCollection.FirstOrDefault(s => s.Key == k && s.Label == l), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var newSetting = keyValueCollection.FirstOrDefault(s => s.Key == setting.Key && s.Label == setting.Label);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var copy = new List<ConfigurationSetting>();
                    foreach (var setting in keyValueCollection)
                    {
                        copy.Add(TestHelpers.CloneSetting(setting));
                    };

                    return new MockAsyncPageable(copy);
                });

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetSettingFromService);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                                      .Register("NonExistentKey", refreshAll: true)
                                      .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            keyValueCollection.ElementAt(0).Value = "newValue1";
            keyValueCollection.ElementAt(1).Value = "newValue2";
            keyValueCollection.Remove(keyValueCollection.FirstOrDefault(s => s.Key == "TestKey3" && s.Label == "label"));

            // Wait for the cache to expire
            Thread.Sleep(1500);

            refresher.RefreshAsync().Wait();

            // Validate that key-values registered for refresh were updated
            Assert.Equal("newValue1", config["TestKey1"]);

            // Validate that other key-values were not updated, which means refresh all wasn't triggered
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
        }

        [Fact]
        public void RefreshTests_SingleServerCallOnSimultaneousMultipleRefresh()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var requestCount = 0;
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    requestCount++;
                    Thread.Sleep(6000);

                    var copy = new List<ConfigurationSetting>();
                    foreach (var setting in keyValueCollection)
                    {
                        copy.Add(TestHelpers.CloneSetting(setting));
                    };

                    return new MockAsyncPageable(copy);
                });

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                requestCount++;
                Thread.Sleep(6000);

                var newSetting = keyValueCollection.FirstOrDefault(s => s.Key == setting.Key && s.Label == setting.Label);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                                      .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal(1, requestCount);

            keyValueCollection.First().Value = "newValue";

            // Simulate simultaneous refresh calls with expired cache from multiple threads
            var task1 = Task.Run(() => WaitAndRefresh(refresher, 1500));
            var task2 = Task.Run(() => WaitAndRefresh(refresher, 3000));
            var task3 = Task.Run(() => WaitAndRefresh(refresher, 4500));
            Task.WaitAll(task1, task2, task3);

            Assert.Equal("newValue", config["TestKey1"]);
            Assert.Equal(2, requestCount);
        }

        [Fact]
        public void RefreshTests_RefreshAsyncThrowsOnRequestFailedException()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException("Request failed."));

            // Wait for the cache to expire
            Thread.Sleep(1500);

            Action action = () => refresher.RefreshAsync().Wait();
            Assert.Throws<AggregateException>(action);

            Assert.NotEqual("newValue", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_TryRefreshAsyncReturnsFalseOnRequestFailedException()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException("Request failed."));

            // Wait for the cache to expire
            Thread.Sleep(1500);

            bool result = refresher.TryRefreshAsync().Result;
            Assert.False(result);

            Assert.NotEqual("newValue", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_TryRefreshAsyncUpdatesConfigurationAndReturnsTrueOnSuccess()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            // Wait for the cache to expire
            Thread.Sleep(1500);

            bool result = refresher.TryRefreshAsync().Result;
            Assert.True(result);

            Assert.Equal("newValue", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_TryRefreshAsyncReturnsFalseForAuthenticationFailedException()
        {
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.Select(setting => TestHelpers.CloneSetting(setting)).ToList()));

            var innerException = new AuthenticationFailedException("Authentication failed.") { Source = "Azure.Identity" };

            mockClient.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == "TestKey1"), mockResponse.Object)))
                .Throws(new KeyVaultReferenceException(innerException.Message, innerException));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            // Wait for the cache to expire
            Thread.Sleep(1500);

            // First call to GetConfigurationSettingAsync does not throw
            Assert.True(refresher.TryRefreshAsync().Result);

            // Wait for the cache to expire
            Thread.Sleep(1500);

            // Second call to GetConfigurationSettingAsync throws KeyVaultReferenceException
            Assert.False(refresher.TryRefreshAsync().Result);
        }

        [Fact]
        public async Task RefreshTests_RefreshAsyncThrowsOnExceptionWhenOptionalIsTrueForInitialLoad()
        {
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var newSetting = _kvCollection.FirstOrDefault(s => s.Key == setting.Key && s.Label == setting.Label);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.Select(setting => TestHelpers.CloneSetting(setting)).ToList()))
                .Throws(new RequestFailedException("Request failed."));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", refreshAll: true)
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                }, optional: true)
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            // Wait for the cache to expire
            Thread.Sleep(1500);

            await Assert.ThrowsAsync<RequestFailedException>(async () =>
                await refresher.RefreshAsync()
            );
        }

        [Fact]
        public async Task RefreshTests_UpdatesAllSettingsIfInitialLoadFails()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException("Request failed"))
                .Throws(new RequestFailedException("Request failed"))
                .Returns(new MockAsyncPageable(_kvCollection));

            mockClient.SetupSequence(c => c.GetConfigurationSettingAsync("TestKey1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == "TestKey1" && s.Label == "label"), mockResponse.Object)));

            IConfigurationRefresher refresher = null;
            IConfiguration configuration = new ConfigurationBuilder()
            .AddAzureAppConfiguration(options =>
            {
                options.Select("TestKey*");
                options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                options.ConfigureRefresh(refreshOptions =>
                {
                    refreshOptions.Register("TestKey1", "label")
                        .SetCacheExpiration(TimeSpan.FromSeconds(1));
                });

                refresher = options.GetRefresher();
            }, optional: true)
            .Build();

            // Validate initial load failed to retrieve any setting
            Assert.Null(configuration["TestKey1"]);
            Assert.Null(configuration["TestKey2"]);
            Assert.Null(configuration["TestKey3"]);

            // Act
            await Assert.ThrowsAsync<RequestFailedException>(async () =>
            {
                await refresher.RefreshAsync();
            });

            await refresher.RefreshAsync();

            Assert.Null(configuration["TestKey1"]);
            Assert.Null(configuration["TestKey2"]);
            Assert.Null(configuration["TestKey3"]);

            // Wait for the cache to expire
            Thread.Sleep(1500);

            await refresher.RefreshAsync();

            // Validate all settings were loaded, including the ones not registered for refresh
            Assert.Equal("TestValue1", configuration["TestKey1"]);
            Assert.Equal("TestValue2", configuration["TestKey2"]);
            Assert.Equal("TestValue3", configuration["TestKey3"]);
        }

        [Fact]
        public void RefreshTests_SetDirtyForcesNextRefresh()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromDays(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            refresher.RefreshAsync().Wait();
            Assert.Equal("TestValue1", config["TestKey1"]);

            refresher.SetDirty(TimeSpan.FromSeconds(1));

            // Wait for the cache to expire based on the randomized delay in SetDirty()
            Thread.Sleep(1200);

            refresher.RefreshAsync().Wait();
            Assert.Equal("newValue", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_SentinelKeyNotUpdatedOnRefreshAllFailure()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var newSetting = keyValueCollection.FirstOrDefault(s => s.Key == setting.Key);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(keyValueCollection.Select(setting => TestHelpers.CloneSetting(setting)).ToList()))
                .Throws(new RequestFailedException(429, "Too many requests"))
                .Returns(new MockAsyncPageable(keyValueCollection.Select(setting =>
                {
                    setting.Value = "newValue";
                    return TestHelpers.CloneSetting(setting);
                }).ToList()));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*", "label");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", refreshAll: true)
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            keyValueCollection.ForEach(kv => kv.Value = "newValue");

            // Wait for the cache to expire
            Thread.Sleep(1500);

            bool firstRefreshResult = refresher.TryRefreshAsync().Result;
            Assert.False(firstRefreshResult);

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            // Wait for the cache to expire
            Thread.Sleep(1500);

            bool secondRefreshResult = refresher.TryRefreshAsync().Result;
            Assert.True(secondRefreshResult);

            Assert.Equal("newValue", config["TestKey1"]);
            Assert.Equal("newValue", config["TestKey2"]);
            Assert.Equal("newValue", config["TestKey3"]);
        }

        [Fact]
        public void RefreshTests_RefreshAllTrueForOverwrittenSentinelUpdatesEntireConfiguration()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKeyWithMultipleLabels", "label1", refreshAll: true)
                                      .Register("TestKeyWithMultipleLabels", "label2")
                                      .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Equal("TestValueForLabel2", config["TestKeyWithMultipleLabels"]);

            keyValueCollection.ForEach(kv => kv.Value = "newValue");

            // Wait for the cache to expire
            Thread.Sleep(1500);

            refresher.RefreshAsync().Wait();

            Assert.Equal("newValue", config["TestKey1"]);
            Assert.Equal("newValue", config["TestKey2"]);
            Assert.Equal("newValue", config["TestKey3"]);
            Assert.Equal("newValue", config["TestKeyWithMultipleLabels"]);
        }

        [Fact]
        public void RefreshTests_RefreshAllFalseForOverwrittenSentinelUpdatesConfig()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            ConfigurationSetting refreshRegisteredSetting = keyValueCollection.FirstOrDefault(s => s.Key == "TestKeyWithMultipleLabels" && s.Label == "label1");
            var mockClient = GetMockConfigurationClient();
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKeyWithMultipleLabels", "label1")
                                      .Register("TestKeyWithMultipleLabels", "label2")
                                      .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Equal("TestValueForLabel2", config["TestKeyWithMultipleLabels"]);

            refreshRegisteredSetting.Value = "UpdatedValueForLabel1";

            // Wait for the cache to expire
            Thread.Sleep(1500);

            refresher.RefreshAsync().Wait();

            // Validate that refresh registered key-value was updated
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Equal("UpdatedValueForLabel1", config["TestKeyWithMultipleLabels"]);
        }

        [Fact]
        public void RefreshTests_RefreshRegisteredKvOverwritesSelectedKv()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            ConfigurationSetting refreshAllRegisteredSetting = keyValueCollection.FirstOrDefault(s => s.Key == "TestKeyWithMultipleLabels" && s.Label == "label1");
            var mockClient = GetMockConfigurationClient();
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKeyWithMultipleLabels", "label1")
                                      .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            // Validate that the value for config["TestKeyWithMultipleLabels"] has been overwritten by refresh registered key-value
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Equal("TestValueForLabel1", config["TestKeyWithMultipleLabels"]);

            refreshAllRegisteredSetting.Value = "UpdatedValueForLabel1";

            // Wait for the cache to expire
            Thread.Sleep(1500);

            refresher.RefreshAsync().Wait();

            // Validate that only the refresh registered key-value was updated
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Equal("UpdatedValueForLabel1", config["TestKeyWithMultipleLabels"]);
        }

        [Fact]
        public void RefreshTests_MultipleRefreshRegistrationsForSameKeyAndDifferentLabel()
        {
            var refreshOptions = new AzureAppConfigurationRefreshOptions()
                .Register("TestKeyWithMultipleLabels")
                .Register("TestKeyWithMultipleLabels", "label1")
                .Register("TestKeyWithMultipleLabels", "label2")
                .Register("TestKeyWithMultipleLabels", "label1")     // Duplicate registration
                .Register("TestKeyWithMultipleLabels", "label2");    // Duplicate registration

            Assert.Equal(3, refreshOptions.RefreshRegistrations.Count);
        }

        [Fact]
        public void RefreshTests_AzureAppConfigurationRefresherProviderReturnsRefreshers()
        {
            static void optionsInitializer(AzureAppConfigurationOptions options)
            {
                options.Connect(TestHelpers.CreateMockEndpointString());
                options.ConfigureClientOptions(clientOptions => clientOptions.Retry.MaxRetries = 0);
            }

            IConfiguration configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(optionsInitializer, optional: true)
                .AddAzureAppConfiguration(optionsInitializer, optional: true)
                .Build();

            IConfigurationRefresherProvider refresherProvider = new AzureAppConfigurationRefresherProvider(configuration, NullLoggerFactory.Instance);
            Assert.Equal(2, refresherProvider.Refreshers.Count());
        }

        [Fact]
        public void RefreshTests_AzureAppConfigurationRefresherProviderThrowsIfNoRefresher()
        {
            IConfiguration configuration = new ConfigurationBuilder().Build();
            void action() => new AzureAppConfigurationRefresherProvider(configuration, NullLoggerFactory.Instance);
            Assert.Throws<InvalidOperationException>(action);
        }

        [Fact]
        public void RefreshTests_ConfigureRefreshThrowsOnNoRegistration()
        {
            void action() => new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });
                })
                .Build();

            Assert.Throws<ArgumentException>(action);
        }

        [Fact]
        public void RefreshTests_RefreshIsCancelled()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            // Wait for the cache to expire
            Thread.Sleep(1500);

            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.CancelAfter(TimeSpan.Zero);  
            Action action = () => refresher.RefreshAsync(cancellationSource.Token).Wait();
            var exception =  Assert.Throws<AggregateException>(action);
            Assert.IsType<TaskCanceledException>(exception.InnerException);
            Assert.Equal("TestValue1", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_UpdateCacheExpirationTimeForFailedRefreshOperations()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(2));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException("Request failed."));

            // Wait for the cache to expire
            Thread.Sleep(2500);

            bool result = refresher.TryRefreshAsync().Result;
            Assert.False(result);

            // refresh again will be no-op
            result = refresher.TryRefreshAsync().Result;
            Assert.True(result);

            Assert.NotEqual("newValue", config["TestKey1"]);
        }

        private void WaitAndRefresh(IConfigurationRefresher refresher, int millisecondsDelay)
        {
            Task.Delay(millisecondsDelay).Wait();
            refresher.RefreshAsync().Wait();
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

        private Mock<ConfigurationClient> GetMockConfigurationClientSelectKeyLabel()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            MockAsyncPageable GetTestKeys(SettingSelector selector, CancellationToken ct)
            {
                var copy = new List<ConfigurationSetting>();
                var newSetting = _kvCollection.FirstOrDefault(s => (s.Key == selector.KeyFilter && s.Label == selector.LabelFilter));
                if (newSetting != null)
                    copy.Add(TestHelpers.CloneSetting(newSetting));
                return new MockAsyncPageable(copy);
            }

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                return Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == key && s.Label == label), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var newSetting = _kvCollection.FirstOrDefault(s => (s.Key == setting.Key && s.Label == setting.Label));
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns((Func<SettingSelector, CancellationToken, MockAsyncPageable>)GetTestKeys);
            
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);
            
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            return mockClient;
        }
    }
}
