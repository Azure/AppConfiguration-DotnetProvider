using Azure;
using Azure.Core.Http;
using Azure.Data.AppConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.AppConfiguration.AspNetCore;
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
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetSettingsAsync(new SettingSelector("*", "\0"), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection));

            var options = new AzureAppConfigurationOptions { Client = mockClient.Object };

            options.ConfigureRefresh(refresh =>
            {
                refresh.Register("TestKey1")
                       .SetCacheExpiration(TimeSpan.FromSeconds(60));
            });

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options)
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_RefreshRegisteredKeysAreLoadedOnStartup_CustomUseQuery()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection));

            mockClient.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(mockResponse.Object, FirstKeyValue));

            var options = new AzureAppConfigurationOptions { Client = mockClient.Object };

            options.Use("TestKey1")
                   .ConfigureRefresh(refreshOptions =>
                   {
                       refreshOptions.Register("TestKey2")
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

        [Fact]
        public void RefreshTests_RefreshIsSkippedIfCacheIsNotExpired()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.Get("Key", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Response.FromValue(mockResponse.Object, FirstKeyValue));

            var options = new AzureAppConfigurationOptions { Client = mockClient.Object };

            options.Use("TestKey*")
                       .ConfigureRefresh(refresh =>
                       {
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

            options.GetRefresher().Refresh().Wait();

            Assert.Equal("TestValue1", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_RefreshIsNotSkippedIfCacheIsExpired()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.Get("Key", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Response.FromValue(mockResponse.Object, FirstKeyValue));

            var options = new AzureAppConfigurationOptions { Client = mockClient.Object };

            options.Use("TestKey*")
                       .ConfigureRefresh(refresh =>
                       {
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

            options.GetRefresher().Refresh().Wait();

            Assert.Equal("newValue", config["TestKey1"]);
        }

        [Fact]
        public void RefreshTests_RefreshAllFalseDoesNotUpdateEntireConfiguration()
        {
            var kvCollection = new List<ConfigurationSetting>(_kvCollection);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.Get("Key", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Response.FromValue(mockResponse.Object, kvCollection.First()));

            var options = new AzureAppConfigurationOptions { Client = mockClient.Object };

            options.Use("TestKey*")
                   .ConfigureRefresh(refresh =>
                   {
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

        [Fact]
        public void RefreshTests_RefreshAllTrueUpdatesEntireConfiguration()
        {
            var kvCollection = new List<ConfigurationSetting>(_kvCollection);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.Get("Key", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Response.FromValue(mockResponse.Object, kvCollection.First()));

            var options = new AzureAppConfigurationOptions { Client = mockClient.Object };

            options.Use("TestKey*")
                       .ConfigureRefresh(refresh =>
                       {
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

        [Fact]
        public void RefreshTests_RefreshAllTrueRemovesDeletedConfiguration()
        {
            var kvCollection = new List<ConfigurationSetting>(_kvCollection);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.Get("Key", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Response.FromValue(mockResponse.Object, kvCollection.First()));

            var options = new AzureAppConfigurationOptions { Client = mockClient.Object };

            options.Use("TestKey*")
                       .ConfigureRefresh(refresh =>
                       {
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

        [Fact]
        public void RefreshTests_RefreshAllForNonExistentSentinelDoesNothing()
        {
            var kvCollection = new List<ConfigurationSetting>(_kvCollection);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.Get("Key", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Response.FromValue(mockResponse.Object, kvCollection.First()));

            var options = new AzureAppConfigurationOptions { Client = mockClient.Object };

            options.Use("TestKey*")
                   .ConfigureRefresh(refresh =>
                   {
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

        [Fact]
        public void RefreshTests_SingleServerCallOnSimultaneousMultipleRefresh()
        {
            var kvCollection = new List<ConfigurationSetting>(_kvCollection);

            var requestCount = 0;

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetSettingsAsync(new SettingSelector(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    requestCount++;
                    Thread.Sleep(6000);
                    return new MockAsyncPageable(_kvCollection);
                });

            mockClient.Setup(c => c.GetAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    requestCount++;
                    Thread.Sleep(6000);
                    return Response.FromValue(mockResponse.Object, kvCollection.First());
                });

            var options = new AzureAppConfigurationOptions { Client = mockClient.Object };

            options.ConfigureRefresh(refresh =>
            {
                refresh.Register("TestKey1", "label")
                       .SetCacheExpiration(TimeSpan.FromSeconds(1));
            });

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options)
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal(1, requestCount);

            kvCollection.First().Value = "newValue";

            // Simulate simultaneous refresh calls with expired cache from multiple threads
            var task1 = Task.Run(() => WaitAndRefresh(options, 1500));
            var task2 = Task.Run(() => WaitAndRefresh(options, 3000));
            var task3 = Task.Run(() => WaitAndRefresh(options, 4500));
            Task.WaitAll(task1, task2, task3);

            Assert.Equal("newValue", config["TestKey1"]);
            Assert.Equal(2, requestCount);
        }

        //[Fact]
        //public void RefreshMiddlewareTests_MiddlewareConstructorParsesIConfigurationRefresher()
        //{
        //    // Arrange
        //    var delegateMock = new Mock<RequestDelegate>();
        //    var configuration = new ConfigurationBuilder()
        //        .AddAzureAppConfiguration(new AzureAppConfigurationOptions
        //        {
        //            Client = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kvCollection.First(), _kvCollection))
        //        })
        //        .Build();

        //    // Act
        //    var middleware = new AzureAppConfigurationRefreshMiddleware(delegateMock.Object, configuration);

        //    // Assert
        //    Assert.NotNull(middleware.Refreshers);
        //    Assert.Equal(1, middleware.Refreshers.Count);
        //}

        //[Fact]
        //public void RefreshMiddlewareTests_MiddlewareConstructorParsesMultipleIConfigurationRefreshers()
        //{
        //    // Arrange
        //    var delegateMock = new Mock<RequestDelegate>();
        //    var configuration = new ConfigurationBuilder()
        //        .AddAzureAppConfiguration(new AzureAppConfigurationOptions
        //        {
        //            Client = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kvCollection.First(), _kvCollection))
        //        })
        //        .AddAzureAppConfiguration(new AzureAppConfigurationOptions
        //        {
        //            Client = new AzconfigClient(_connectionString, new MockedGetKeyValueRequest(_kvCollection.Last(), _kvCollection))
        //        })
        //        .Build();

        //    // Act
        //    var middleware = new AzureAppConfigurationRefreshMiddleware(delegateMock.Object, configuration);

        //    // Assert
        //    Assert.NotNull(middleware.Refreshers);
        //    Assert.Equal(2, middleware.Refreshers.Count);
        //}

        [Fact]
        public void RefreshMiddlewareTests_InvalidOperationExceptionOnIConfigurationCastFailure()
        {
            // Arrange
            var delegateMock = new Mock<RequestDelegate>();
            var configMock = new Mock<IConfiguration>();
            Action action = () => new AzureAppConfigurationRefreshMiddleware(delegateMock.Object, configMock.Object);

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
