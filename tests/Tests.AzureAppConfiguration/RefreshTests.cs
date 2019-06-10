using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class RefreshTests
    {
        string _connectionString = TestHelpers.CreateMockEndpointString();

        IEnumerable<IKeyValue> _kvCollection = new List<IKeyValue>
        {
            new KeyValue("TestKey1")
            {
                Label = "label",
                Value = "TestValue1",
                ETag = "0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63",
                ContentType = "text"
            },
            new KeyValue("TestKey2")
            {
                Label = "label",
                Value = "TestValue2",
                ETag = "31c38369-831f-4bf1-b9ad-79db56c8b989",
                ContentType = "text"
            },
            new KeyValue("TestKey3")
            {
                Label = "label",
                Value = "TestValue3",
                ETag = "bb203f2b-c113-44fc-995d-b933c2143339",
                ContentType = "text"
            }
        };

        IKeyValue FirstKeyValue => _kvCollection.First();

        [Fact]
        public void RefreshTests_RefreshRegisteredKeysAreLoadedOnStartup_DefaultUseQuery()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(FirstKeyValue, _kvCollection)))
            {
                var options = new AzureAppConfigurationOptions { Client = testClient };

                options.ConfigureRefresh(refresh => {
                    refresh.Register("TestKey1")
                           .SetCacheExpiration(TimeSpan.FromSeconds(60));
                });

                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options)
                    .Build();

                Assert.Equal("TestValue1", config["TestKey1"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshRegisteredKeysAreLoadedOnStartup_CustomUseQuery()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(FirstKeyValue, _kvCollection)))
            {
                var options = new AzureAppConfigurationOptions { Client = testClient };

                options.Use("RandomKey")
                       .ConfigureRefresh(refresh => {
                           refresh.Register("TestKey1")
                                  .SetCacheExpiration(TimeSpan.FromSeconds(60));
                       });

                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options)
                    .Build();

                Assert.Equal("TestValue1", config["TestKey1"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshIsSkippedIfCacheIsNotExpired()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(FirstKeyValue, _kvCollection)))
            {
                var options = new AzureAppConfigurationOptions { Client = testClient };

                options.Use("TestKey*")
                       .ConfigureRefresh(refresh => {
                           refresh.Register("TestKey1")
                                  .SetCacheExpiration(TimeSpan.FromSeconds(10));
                       });

                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options)
                    .Build();

                Assert.Equal("TestValue1", config["TestKey1"]);
                FirstKeyValue.Value = "newValue1";

                // Wait for some time but not enough to let the cache expire
                Thread.Sleep(5000);

                options.GetRefresher().Refresh();

                Assert.Equal("TestValue1", config["TestKey1"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshIsNotSkippedIfCacheIsExpired()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(FirstKeyValue, _kvCollection)))
            {
                var options = new AzureAppConfigurationOptions { Client = testClient };

                options.Use("TestKey*")
                       .ConfigureRefresh(refresh => {
                           refresh.Register("TestKey1")
                                  .SetCacheExpiration(TimeSpan.FromSeconds(1));
                       });

                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options)
                    .Build();

                Assert.Equal("TestValue1", config["TestKey1"]);
                FirstKeyValue.Value = "newValue";

                // Wait for the cache to expire
                Thread.Sleep(1500);

                options.GetRefresher().Refresh();

                Assert.Equal("newValue", config["TestKey1"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshAllFalseDoesNotUpdateEntireConfiguration()
        {
            var kvCollection = new List<IKeyValue>(_kvCollection);

            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(kvCollection.First(), kvCollection)))
            {
                var options = new AzureAppConfigurationOptions { Client = testClient };

                options.Use("TestKey*")
                       .ConfigureRefresh(refresh => {
                           refresh.Register("TestKey1") // refreshAll: false
                                  .SetCacheExpiration(TimeSpan.FromSeconds(1));
                       });

                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options)
                    .Build();

                Assert.Equal("TestValue1", config["TestKey1"]);
                Assert.Equal("TestValue2", config["TestKey2"]);
                Assert.Equal("TestValue3", config["TestKey3"]);

                kvCollection.ForEach(kv => kv.Value = "newValue");

                // Wait for the cache to expire
                Thread.Sleep(1500);

                options.GetRefresher().Refresh();

                Assert.Equal("newValue", config["TestKey1"]);
                Assert.NotEqual("newValue", config["TestKey2"]);
                Assert.NotEqual("newValue", config["TestKey3"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshAllTrueUpdatesEntireConfiguration()
        {
            var kvCollection = new List<IKeyValue>(_kvCollection);

            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(kvCollection.First(), kvCollection)))
            {
                var options = new AzureAppConfigurationOptions { Client = testClient };

                options.Use("TestKey*")
                       .ConfigureRefresh(refresh => {
                           refresh.Register("TestKey1", refreshAll: true)
                                  .SetCacheExpiration(TimeSpan.FromSeconds(1));
                       });

                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options)
                    .Build();

                Assert.Equal("TestValue1", config["TestKey1"]);
                Assert.Equal("TestValue2", config["TestKey2"]);
                Assert.Equal("TestValue3", config["TestKey3"]);

                kvCollection.ForEach(kv => kv.Value = "newValue");

                // Wait for the cache to expire
                Thread.Sleep(1500);

                options.GetRefresher().Refresh();

                Assert.Equal("newValue", config["TestKey1"]);
                Assert.Equal("newValue", config["TestKey2"]);
                Assert.Equal("newValue", config["TestKey3"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshAllTrueRemovesDeletedConfiguration()
        {
            var kvCollection = new List<IKeyValue>(_kvCollection);

            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(kvCollection.First(), kvCollection)))
            {
                var options = new AzureAppConfigurationOptions { Client = testClient };

                options.Use("TestKey*")
                       .ConfigureRefresh(refresh => {
                           refresh.Register("TestKey1", refreshAll: true)
                                  .SetCacheExpiration(TimeSpan.FromSeconds(1));
                       });

                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options)
                    .Build();

                Assert.Equal("TestValue1", config["TestKey1"]);
                Assert.Equal("TestValue2", config["TestKey2"]);
                Assert.Equal("TestValue3", config["TestKey3"]);

                kvCollection.First().Value = "newValue";
                kvCollection.Remove(kvCollection.Last());

                // Wait for the cache to expire
                Thread.Sleep(1500);

                options.GetRefresher().Refresh();

                Assert.Equal("newValue", config["TestKey1"]);
                Assert.Equal("TestValue2", config["TestKey2"]);
                Assert.Null(config["TestKey3"]);
            }
        }
    }
}
