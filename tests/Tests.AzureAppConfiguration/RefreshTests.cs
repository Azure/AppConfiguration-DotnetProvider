using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
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

        [Fact]
        public void RefreshTests_RefreshRegisteredKeysAreLoadedOnStartup_DefaultUseQuery()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kvCollection)))
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
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kvCollection)))
            {
                var options = new AzureAppConfigurationOptions { Client = testClient };

                options.Use("TestKey1")
                       .ConfigureRefresh(refresh => {
                           refresh.Register("TestKey2")
                                  .Register("TestKey3")
                                  .SetCacheExpiration(TimeSpan.FromSeconds(60));
                       });

                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options)
                    .Build();

                Assert.Equal("TestValue1", config["TestKey1"]);
                Assert.Equal("TestValue2", config["TestKey2"]);
                Assert.Equal("TestValue3", config["TestKey3"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshIsSkippedIfCacheIsNotExpired()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kvCollection)))
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
                _kvCollection.First().Value = "newValue1";

                // Wait for some time but not enough to let the cache expire
                Thread.Sleep(5000);

                options.GetRefresher().Refresh().Wait();

                Assert.Equal("TestValue1", config["TestKey1"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshIsNotSkippedIfCacheIsExpired()
        {
            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kvCollection)))
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
                _kvCollection.First().Value = "newValue";

                // Wait for the cache to expire
                Thread.Sleep(1500);

                options.GetRefresher().Refresh().Wait();

                Assert.Equal("newValue", config["TestKey1"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshAllFalseDoesNotUpdateEntireConfiguration()
        {
            var kvCollection = new List<IKeyValue>(_kvCollection);

            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(kvCollection)))
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

                options.GetRefresher().Refresh().Wait();

                Assert.Equal("newValue", config["TestKey1"]);
                Assert.NotEqual("newValue", config["TestKey2"]);
                Assert.NotEqual("newValue", config["TestKey3"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshAllTrueUpdatesEntireConfiguration()
        {
            var kvCollection = new List<IKeyValue>(_kvCollection);

            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(kvCollection)))
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

                options.GetRefresher().Refresh().Wait();

                Assert.Equal("newValue", config["TestKey1"]);
                Assert.Equal("newValue", config["TestKey2"]);
                Assert.Equal("newValue", config["TestKey3"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshAllTrueRemovesDeletedConfiguration()
        {
            var kvCollection = new List<IKeyValue>(_kvCollection);

            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(kvCollection)))
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

                options.GetRefresher().Refresh().Wait();

                Assert.Equal("newValue", config["TestKey1"]);
                Assert.Equal("TestValue2", config["TestKey2"]);
                Assert.Null(config["TestKey3"]);
            }
        }

        [Fact]
        public void RefreshTests_RefreshAllForNonExistentSentinelDoesNothing()
        {
            var kvCollection = new List<IKeyValue>(_kvCollection);

            using (var testClient = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(kvCollection)))
            {
                var options = new AzureAppConfigurationOptions { Client = testClient };

                options.Use("TestKey*")
                       .ConfigureRefresh(refresh => {
                           refresh.Register("TestKey1")
                                  .Register("NonExistentKey", refreshAll: true)
                                  .SetCacheExpiration(TimeSpan.FromSeconds(1));
                       });

                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options)
                    .Build();

                Assert.Equal("TestValue1", config["TestKey1"]);
                Assert.Equal("TestValue2", config["TestKey2"]);
                Assert.Equal("TestValue3", config["TestKey3"]);

                kvCollection.ElementAt(0).Value = "newValue1";
                kvCollection.ElementAt(1).Value = "newValue2";
                kvCollection.Remove(kvCollection.Last());

                // Wait for the cache to expire
                Thread.Sleep(1500);

                options.GetRefresher().Refresh().Wait();

                // Validate that key-values registered for refresh were updated
                Assert.Equal("newValue1", config["TestKey1"]);

                // Validate that other key-values were not updated, which means refresh all wasn't triggered
                Assert.Equal("TestValue2", config["TestKey2"]);
                Assert.Equal("TestValue3", config["TestKey3"]);
            }
        }

        [Fact]
        public void RefreshTests_SingleServerCallOnSimultaneousMultipleRefresh()
        {
            var kvCollection = new List<IKeyValue>(_kvCollection);
            var mockedHttpRequestHandler = new MockedGetKeyValueRequest(kvCollection, 6000);

            using (var testClient = new AzconfigClient(_connectionString, mockedHttpRequestHandler))
            {
                var options = new AzureAppConfigurationOptions { Client = testClient };

                options.ConfigureRefresh(refresh => {
                           refresh.Register("TestKey1", "label")
                                  .SetCacheExpiration(TimeSpan.FromSeconds(1));
                       });

                var config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options)
                    .Build();

                Assert.Equal("TestValue1", config["TestKey1"]);
                Assert.Equal(1, mockedHttpRequestHandler.RequestCount);

                kvCollection.First().Value = "newValue";

                // Simulate simultaneous refresh calls with expired cache from multiple threads
                var task1 = Task.Run(() => WaitAndRefresh(options, 1500));
                var task2 = Task.Run(() => WaitAndRefresh(options, 3000));
                var task3 = Task.Run(() => WaitAndRefresh(options, 4500));
                Task.WaitAll(task1, task2, task3);

                Assert.Equal("newValue", config["TestKey1"]);
                Assert.Equal(2, mockedHttpRequestHandler.RequestCount);
            }
        }

        [Fact]
        public void RefreshExtensionTests_AddAzureAppConfiguration_ParsesMultipleAzureAppConfigurationSources()
        {
            var kvCollection = new List<IKeyValue>(_kvCollection);
            var mockedHttpRequestHandler = new MockedGetKeyValueRequest(kvCollection);

            using (var testClient = new AzconfigClient(_connectionString, mockedHttpRequestHandler))
            {
                var options1 = new AzureAppConfigurationOptions { Client = testClient }
                    .Use("TestKey1", "label")
                    .ConfigureRefresh(refresh => {
                        refresh.Register("TestKey1", "label")
                               .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                var options2 = new AzureAppConfigurationOptions { Client = testClient }
                    .Use("TestKey2", "label")
                    .ConfigureRefresh(refresh => {
                        refresh.Register("TestKey2", "label")
                               .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                var configuration = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options1)
                    .AddAzureAppConfiguration(options2)
                    .Build();

                ServiceProvider serviceProvider = new ServiceCollection()
                    .AddSingleton<IConfiguration>(configuration)
                    .AddAzureAppConfiguration()
                    .BuildServiceProvider();

                var refreshers = serviceProvider.GetServices<IConfigurationRefresher>();

                Assert.Equal("TestValue1", configuration["TestKey1"]);
                Assert.Equal("TestValue2", configuration["TestKey2"]);

                kvCollection.ElementAt(0).Value = "newValue1";
                kvCollection.ElementAt(1).Value = "newValue2";

                // Wait for the cache to expire
                Thread.Sleep(1500);

                // Ensure that there is a single instance of refresher in the service provider and invoke global refresh
                Assert.Single(refreshers);
                refreshers.Single().Refresh().Wait();

                // Validate that both key-values were updated using a single global refresher instance
                Assert.Equal("newValue1", configuration["TestKey1"]);
                Assert.Equal("newValue2", configuration["TestKey2"]);
            }
        }

        [Fact]
        public void RefreshExtensionTests_AddAzureAppConfiguration_ParsesSingleAzureAppConfigurationSource()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(new AzureAppConfigurationOptions
                {
                    Client = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kvCollection))
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddAzureAppConfiguration();

            // Act
            ServiceProvider serviceProvider = services.BuildServiceProvider();

            // Assert
            var refreshers = serviceProvider.GetServices<IConfigurationRefresher>();
            Assert.Single(refreshers);
        }

        [Fact]
        public void RefreshExtensionTests_AddAzureAppConfiguration_ExceptionOnMissingProvider()
        {
            // Arrange
            var configuration = new ConfigurationBuilder()
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            Action action = () => services.AddAzureAppConfiguration();

            // Act and Assert
            Assert.Throws<InvalidOperationException>(action);
        }

        private void WaitAndRefresh(AzureAppConfigurationOptions options, int millisecondsDelay)
        {
            Task.Delay(millisecondsDelay).Wait();
            options.GetRefresher().Refresh().Wait();
        }
    }
}
