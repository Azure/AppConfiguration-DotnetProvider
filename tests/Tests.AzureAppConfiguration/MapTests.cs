// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
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
    public class MapTests
    {
        List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key: "Test__Key1",
                label: null,
                value: "TestValue1",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType: null),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "Test__Key2",
                label: null,
                value: "TestValue2",
                eTag: new ETag("31c38369-831f-4bf1-b9ad-79db56c8b989"),
                contentType: null),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "Test__Key3",
                label: null,
                value: "TestValue3",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: null)
        };

        ConfigurationSetting FirstKeyValue => _kvCollection.First();

        [Fact]
        public void ReplaceSeparatorFromKey()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("*");
                    options.Map((setting) =>
                    {
                        setting.Key = setting.Key.Replace("__", ":");
                        return new ValueTask<ConfigurationSetting>(setting);
                    });
                })
                .Build();

            Assert.True(config["Test:Key1"] == "TestValue1");
            Assert.True(config["Test:Key2"] == "TestValue2");
            Assert.True(config["Test:Key3"] == "TestValue3");
        }

        [Fact]
        public void MultipleMappers()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("*");
                    options.Map((setting) =>
                    {
                        setting.Key = setting.Key.Replace("__", ":");
                        return new ValueTask<ConfigurationSetting>(setting);
                    });
                    options.Map((setting) =>
                    {
                        setting.Key = setting.Key.Replace("Test:", "Beta/");
                        return new ValueTask<ConfigurationSetting>(setting);
                    });
                })
                .Build();

            Assert.True(config["Beta/Key1"] == "TestValue1");
            Assert.True(config["Beta/Key2"] == "TestValue2");
            Assert.True(config["Beta/Key3"] == "TestValue3");
        }

        [Fact]
        public void MapAndTrimKeyPrefix()
        {
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("*");
                    options.Map((setting) =>
                    {
                        setting.Key = setting.Key.Replace("__", ":");
                        return new ValueTask<ConfigurationSetting>(setting);
                    });
                    options.TrimKeyPrefix("Test:");
                })
                .Build();

            Assert.True(config["Key1"] == "TestValue1");
            Assert.True(config["Key2"] == "TestValue2");
            Assert.True(config["Key3"] == "TestValue3");
        }

        [Fact]
        public void MapAndRefreshModifiedKey()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            var cacheExpirationTimeSpan = TimeSpan.FromSeconds(1);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("Test__Key1")
                            .SetCacheExpiration(cacheExpirationTimeSpan);
                    });
                    options.Map((setting) =>
                    {
                        setting.Key = setting.Key.Replace("__", ":");
                        return new ValueTask<ConfigurationSetting>(setting);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["Test:Key1"]);
            Assert.Equal("TestValue2", config["Test:Key2"]);
            Assert.Equal("TestValue3", config["Test:Key3"]);

            FirstKeyValue.Value = "RefreshedValue";

            Thread.Sleep(cacheExpirationTimeSpan);
            refresher.RefreshAsync().Wait();

            Assert.Equal("RefreshedValue", config["Test:Key1"]);
            Assert.Equal("TestValue2", config["Test:Key2"]);
            Assert.Equal("TestValue3", config["Test:Key3"]);
        }

        [Fact]
        public void MapAndRefreshModifiedValue()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            var cacheExpirationTimeSpan = TimeSpan.FromSeconds(1);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("Test__Key1")
                            .SetCacheExpiration(cacheExpirationTimeSpan);
                    });
                    options.Map((setting) =>
                    {
                        setting.Value = "MapperUpdatedValue";
                        return new ValueTask<ConfigurationSetting>(setting);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("MapperUpdatedValue", config["Test__Key1"]);
            Assert.Equal("MapperUpdatedValue", config["Test__Key2"]);
            Assert.Equal("MapperUpdatedValue", config["Test__Key3"]);

            FirstKeyValue.Value = "RefreshedValue";

            Thread.Sleep(cacheExpirationTimeSpan);
            refresher.RefreshAsync().Wait();

            // Even though the value was refreshed, custom mapping overwrote the new value from server.
            mockClient.Verify(client => client.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);

            Assert.Equal("MapperUpdatedValue", config["Test__Key1"]);
            Assert.Equal("MapperUpdatedValue", config["Test__Key2"]);
            Assert.Equal("MapperUpdatedValue", config["Test__Key3"]);
        }

        [Fact]
        public void MapAndRefreshAll()
        {
            IConfigurationRefresher refresher = null;
            var mockClient = GetMockConfigurationClient();
            var cacheExpirationTimeSpan = TimeSpan.FromSeconds(1);
            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("Test__Key1", refreshAll: true)
                            .SetCacheExpiration(cacheExpirationTimeSpan);
                    });
                    options.Map((setting) =>
                    {
                        setting.Key = setting.Key.Replace("__", ":");
                        return new ValueTask<ConfigurationSetting>(setting);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["Test:Key1"]);
            Assert.Equal("TestValue2", config["Test:Key2"]);
            Assert.Equal("TestValue3", config["Test:Key3"]);

            keyValueCollection.ForEach(kv => kv.Value = "newValue");

            Thread.Sleep(cacheExpirationTimeSpan);
            refresher.RefreshAsync().Wait();

            Assert.Equal("newValue", config["Test:Key1"]);
            Assert.Equal("newValue", config["Test:Key2"]);
            Assert.Equal("newValue", config["Test:Key3"]);
        }

        private Mock<ConfigurationClient> GetMockConfigurationClient()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                return Response.FromValue(TestHelpers.CloneSetting(_kvCollection.FirstOrDefault(s => s.Key == key && s.Label == label)), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
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
