﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
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
        string _connectionString = TestHelpers.CreateMockEndpointString();

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
                contentType: "text")
        };

        ConfigurationSetting FirstKeyValue => _kvCollection.First();

        [Fact]
        public void RefreshTests_RefreshRegisteredKeysAreLoadedOnStartup_DefaultUseQuery()
        {
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureRefresh(refresh =>
                    {
                        refresh.Register("TestKey1")
                               .SetCacheExpiration(TimeSpan.FromSeconds(60));
                    });
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_RefreshRegisteredKeysAreLoadedOnStartup_CustomUseQuery()
        {
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("TestKey1");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey2")
                            .Register("TestKey3")
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
                    options.Client = mockClient.Object;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1")
                            .SetCacheExpiration(TimeSpan.FromSeconds(10));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            // Wait for some time but not enough to let the cache expire
            Thread.Sleep(5000);

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
                    options.Client = mockClient.Object;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1")
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
            var serviceCollection = new List<ConfigurationSetting>(_kvCollection);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            Response<ConfigurationSetting> GetSettingFromService(string k, string l, CancellationToken ct)
            {
                return Response.FromValue(serviceCollection.FirstOrDefault(s => s.Key == k), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool cond, CancellationToken ct)
            {
                var newSetting = serviceCollection.FirstOrDefault(s => s.Key == setting.Key);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var copy = new List<ConfigurationSetting>();
                    foreach (var setting in serviceCollection)
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
                    options.Client = mockClient.Object;
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

            serviceCollection.ForEach(kv => kv.Value = "newValue");

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
            var serviceCollection = new List<ConfigurationSetting>(_kvCollection);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            Response<ConfigurationSetting> GetSettingFromService(string k, string l, CancellationToken ct)
            {
                return Response.FromValue(serviceCollection.FirstOrDefault(s => s.Key == k), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool cond, CancellationToken ct)
            {
                var newSetting = serviceCollection.FirstOrDefault(s => s.Key == setting.Key);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var copy = new List<ConfigurationSetting>();
                    foreach (var setting in serviceCollection)
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
                    options.Client = mockClient.Object;
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

            serviceCollection.ForEach(kv => kv.Value = "newValue");

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
            var serviceCollection = new List<ConfigurationSetting>(_kvCollection);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            Response<ConfigurationSetting> GetSettingFromService(string k, string l, CancellationToken ct)
            {
                return Response.FromValue(serviceCollection.FirstOrDefault(s => s.Key == k), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool cond, CancellationToken ct)
            {
                var newSetting = serviceCollection.FirstOrDefault(s => s.Key == setting.Key);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var copy = new List<ConfigurationSetting>();
                    foreach (var setting in serviceCollection)
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
                    options.Client = mockClient.Object;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", refreshAll: true)
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            serviceCollection.First().Value = "newValue";
            serviceCollection.Remove(serviceCollection.Last());

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
            var serviceCollection = new List<ConfigurationSetting>(_kvCollection);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            Response<ConfigurationSetting> GetSettingFromService(string k, string l, CancellationToken ct)
            {
                return Response.FromValue(serviceCollection.FirstOrDefault(s => s.Key == k), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool cond, CancellationToken ct)
            {
                var newSetting = serviceCollection.FirstOrDefault(s => s.Key == setting.Key);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    var copy = new List<ConfigurationSetting>();
                    foreach (var setting in serviceCollection)
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
                    options.Client = mockClient.Object;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1")
                                      .Register("NonExistentKey", refreshAll: true)
                                      .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            serviceCollection.ElementAt(0).Value = "newValue1";
            serviceCollection.ElementAt(1).Value = "newValue2";
            serviceCollection.Remove(serviceCollection.Last());

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
            var serviceCollection = new List<ConfigurationSetting>(_kvCollection);

            var requestCount = 0;

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    requestCount++;
                    Thread.Sleep(6000);

                    var copy = new List<ConfigurationSetting>();
                    foreach (var setting in serviceCollection)
                    {
                        copy.Add(TestHelpers.CloneSetting(setting));
                    };

                    return new MockAsyncPageable(copy);
                });

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool cond, CancellationToken ct)
            {
                requestCount++;
                Thread.Sleep(6000);

                var newSetting = serviceCollection.FirstOrDefault(s => s.Key == setting.Key);
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
                    options.Client = mockClient.Object;
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

            serviceCollection.First().Value = "newValue";

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
                    options.Client = mockClient.Object;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
                    options.Client = mockClient.Object;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
                    options.Client = mockClient.Object;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1")
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
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection));

            var innerException = new AuthenticationFailedException("Authentication failed.") { Source = "Azure.Identity" };
            mockClient.SetupSequence(c => c.GetConfigurationSettingAsync("TestKey1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == "TestKey1"), mockResponse.Object)))
                .Returns(Task.FromResult(Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == "TestKey1"), mockResponse.Object)))
                .Throws(new KeyVaultReferenceException(innerException.Message, innerException));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue";

            // Wait for the cache to expire
            Thread.Sleep(1500);

            // Second call to GetConfigurationSettingAsync does not throw
            Assert.True(refresher.TryRefreshAsync().Result);

            // Wait for the cache to expire
            Thread.Sleep(1500);

            // Third call to GetConfigurationSettingAsync throws KeyVaultReferenceException
            Assert.False(refresher.TryRefreshAsync().Result);
        }

        [Fact]
        public async Task RefreshTests_RefreshAsyncThrowsOnExceptionWhenOptionalIsTrueForInitialLoad()
        {
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection))
                .Throws(new RequestFailedException("Request failed."));

            mockClient.SetupSequence(c => c.GetConfigurationSettingAsync("TestKey1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == "TestKey1"), mockResponse.Object)))
                .Returns(Task.FromResult(Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == "TestKey1"), mockResponse.Object)));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", refreshAll: true)
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
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException("Request failed"))
                .Throws(new RequestFailedException("Request failed"))
                .Returns(new MockAsyncPageable(_kvCollection));

            mockClient.SetupSequence(c => c.GetConfigurationSettingAsync("TestKey1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == "TestKey1"), mockResponse.Object)));

            IConfigurationRefresher refresher = null;
            IConfiguration configuration = new ConfigurationBuilder()
            .AddAzureAppConfiguration(options =>
            {
                options.Select("TestKey*");
                options.Client = mockClient.Object;
                options.ConfigureRefresh(refreshOptions =>
                {
                    refreshOptions.Register("TestKey1")
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
                    options.Client = mockClient.Object;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1")
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
            var serviceCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool cond, CancellationToken ct)
            {
                var newSetting = serviceCollection.FirstOrDefault(s => s.Key == setting.Key);
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(serviceCollection.Select(setting => TestHelpers.CloneSetting(setting)).ToList()))
                .Throws(new RequestFailedException(429, "Too many requests"))
                .Returns(new MockAsyncPageable(serviceCollection.Select(setting =>
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
                    options.Client = mockClient.Object;
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

            serviceCollection.ForEach(kv => kv.Value = "newValue");

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

            IConfigurationRefresherProvider refresherProvider = new AzureAppConfigurationRefresherProvider(configuration);
            Assert.Equal(2, refresherProvider.Refreshers.Count());
        }

        [Fact]
        public void RefreshTests_AzureAppConfigurationRefresherProviderThrowsIfNoRefresher()
        {
            IConfiguration configuration = new ConfigurationBuilder().Build();
            void action() => new AzureAppConfigurationRefresherProvider(configuration);
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

        private void WaitAndRefresh(IConfigurationRefresher refresher, int millisecondsDelay)
        {
            Task.Delay(millisecondsDelay).Wait();
            refresher.RefreshAsync().Wait();
        }

        private Mock<ConfigurationClient> GetMockConfigurationClient()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            Response<ConfigurationSetting> GetTestKey(string k, string l, CancellationToken ct)
            {
                return Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == k), mockResponse.Object);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection));
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            return mockClient;
        }
    }
}
