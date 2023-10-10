// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
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
    public class FailOverTests
    {
        readonly ConfigurationSetting kv = ConfigurationModelFactory.ConfigurationSetting(key: "TestKey1", label: "label", value: "TestValue1",
                                                                                          eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                                                                                          contentType: "text");

        [Fact]
        public void FailOverTests_DoesNotReturnBackedOffClient()
        {
            // Arrange
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();

            var mockClient1 = new Mock<ConfigurationClient>();
            mockClient1.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            var mockClient2 = new Mock<ConfigurationClient>();
            mockClient2.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)));
            mockClient2.Setup(c => c.Equals(mockClient2)).Returns(true);

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, mockClient1.Object);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var clientList = new List<ConfigurationClientWrapper>() { cw1, cw2 };
            var configClientManager = new ConfigurationClientManager(clientList);

            // The client enumerator should return 2 clients for the first time.
            Assert.Equal(2, configClientManager.GetAvailableClients(DateTimeOffset.UtcNow, CancellationToken.None).GetAwaiter().GetResult().Count());

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = configClientManager;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });
                    options.ReplicaDiscoveryEnabled = true;

                    refresher = options.GetRefresher();
                })
                .Build();

            // The client enumerator should return just 1 client since one client is in the backoff state.
            Assert.Single(configClientManager.GetAvailableClients(DateTimeOffset.UtcNow, CancellationToken.None).GetAwaiter().GetResult());
        }

        [Fact]
        public void FailOverTests_ReturnsAllClientsIfAllBackedOff()
        {
            // Arrange
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();

            var mockClient1 = new Mock<ConfigurationClient>();
            mockClient1.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            var mockClient2 = new Mock<ConfigurationClient>();
            mockClient2.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient2.Setup(c => c.Equals(mockClient2)).Returns(true);

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, mockClient1.Object);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var clientList = new List<ConfigurationClientWrapper>() { cw1, cw2 };
            var configClientManager = new ConfigurationClientManager(clientList);

            // The client enumerator should return 2 clients for the first time.
            Assert.Equal(2, configClientManager.GetAvailableClients(DateTimeOffset.UtcNow, CancellationToken.None).GetAwaiter().GetResult().Count());

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = configClientManager;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    options.ReplicaDiscoveryEnabled = false;
                   
                    refresher = options.GetRefresher();
                });

            // Throws last exception when all clients fail.
            Assert.Throws<RequestFailedException>(configBuilder.Build);

            // The client manager should return no clients since all clients are in the back-off state.
            Assert.False(configClientManager.GetAvailableClients(DateTimeOffset.UtcNow, CancellationToken.None).GetAwaiter().GetResult().Any());
        }

        [Fact]
        public void FailOverTests_PropagatesNonFailOverableExceptions()
        {
            // Arrange
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();

            var mockClient1 = new Mock<ConfigurationClient>();
            mockClient1.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(404, "Not found."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(404, "Not found."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(404, "Not found."));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            var mockClient2 = new Mock<ConfigurationClient>();
            mockClient2.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient2.Setup(c => c.Equals(mockClient2)).Returns(true);

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, mockClient1.Object);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var clientList = new List<ConfigurationClientWrapper>() { cw1, cw2 };
            var configClientManager = new ConfigurationClientManager(clientList);

            // The client enumerator should return 2 clients for the first time.
            Assert.Equal(2, configClientManager.GetAvailableClients(DateTimeOffset.UtcNow, CancellationToken.None).GetAwaiter().GetResult().Count());

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = configClientManager;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                });

            // Throws last exception when all clients fail.
            Assert.Throws<RequestFailedException>(configBuilder.Build);
        }

        [Fact]
        public void FailOverTests_BackoffStateIsUpdatedOnSuccessfulRequest()
        {
            // Arrange
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();

            var mockClient1 = new Mock<ConfigurationClient>();
            mockClient1.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient1.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)));
            mockClient1.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            var mockClient2 = new Mock<ConfigurationClient>();
            mockClient2.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient2.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)));
            mockClient2.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)));
            mockClient2.Setup(c => c.Equals(mockClient2)).Returns(true);

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, mockClient1.Object);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var clientList = new List<ConfigurationClientWrapper>() { cw1, cw2 };
            var configClientManager = new ConfigurationClientManager(clientList);

            // The client enumerator should return 2 clients for the first time.
            Assert.Equal(2, configClientManager.GetAvailableClients(DateTimeOffset.UtcNow, CancellationToken.None).GetAwaiter().GetResult().Count());

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = configClientManager;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                }).Build();

            // The client enumerator should return just 1 client for the second time.
            Assert.Single(configClientManager.GetAvailableClients(DateTimeOffset.UtcNow, CancellationToken.None).GetAwaiter().GetResult());

            // Sleep for backoff-time to pass.
            Thread.Sleep(TimeSpan.FromSeconds(31));

            refresher.RefreshAsync().Wait();

            // The client enumerator should return 2 clients for the third time.
            Assert.Equal(2, configClientManager.GetAvailableClients(DateTimeOffset.UtcNow, CancellationToken.None).GetAwaiter().GetResult().Count());
        }

        [Fact]
        public void FailOverTests_AutoFailover()
        {
            // Arrange
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();

            var mockClient1 = new Mock<ConfigurationClient>();
            mockClient1.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            var mockClient2 = new Mock<ConfigurationClient>();
            mockClient2.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse.Object)));
            mockClient2.Setup(c => c.Equals(mockClient2)).Returns(true);

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, mockClient1.Object);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var clientList = new List<ConfigurationClientWrapper>() { cw1 };
            var autoFailoverList = new List<ConfigurationClientWrapper>() { cw2 };
            var mockedConfigClientManager = new MockedConfigurationClientManager(clientList, autoFailoverList);

            // Should not throw exception.
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockedConfigClientManager;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();
        }
    }
}
