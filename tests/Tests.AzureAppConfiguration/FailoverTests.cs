// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
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
    public class FailOverTests
    {
        readonly ConfigurationSetting kv = ConfigurationModelFactory.ConfigurationSetting(key: "TestKey1", label: "label", value: "TestValue1",
                                                                                          eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                                                                                          contentType: "text");

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

            // The client enumerator should return 2 clients
            Assert.Equal(2, configClientManager.GetClients().Count());

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConfigureStartupOptions(startupOptions =>
                    {
                        startupOptions.Timeout = TimeSpan.FromSeconds(15);
                    });
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
            Exception exception = Assert.Throws<TimeoutException>(() => configBuilder.Build());

            // Assert the inner aggregate exception
            Assert.IsType<AggregateException>(exception.InnerException);

            // Assert the inner request failed exceptions
            Assert.True((exception.InnerException as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false);

            refresher.RefreshAsync().Wait();

            // The client manager should have called RefreshClients when all clients were backed off
            Assert.Equal(1, configClientManager.RefreshClientsCalled);
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

            // The client enumerator should return 2 clients
            Assert.Equal(2, configClientManager.GetClients().Count());

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

            // The client enumerator should return 2 clients
            Assert.Equal(2, configClientManager.GetClients().Count());

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.MinBackoffDuration = TimeSpan.FromSeconds(2);
                    options.ClientManager = configClientManager;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                }).Build();

            refresher.RefreshAsync().Wait();


            // The first client should have been called just once
            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(0));

            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(0));

            mockClient2.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(1));

            // Wait for client 1 backoff to end
            Thread.Sleep(2500);
            
            refresher.RefreshAsync().Wait();

            // The first client should have been called now with refresh
            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
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

        [Fact]
        public void FailOverTests_ValidateEndpoints()
        {
            var configClientManager = new ConfigurationClientManager(
                new[] { new Uri("https://foobar.azconfig.io") },
                new DefaultAzureCredential(),
                new ConfigurationClientOptions(),
                true);

            Assert.True(configClientManager.IsValidEndpoint("azure.azconfig.io"));
            Assert.True(configClientManager.IsValidEndpoint("appconfig.azconfig.io"));
            Assert.True(configClientManager.IsValidEndpoint("azure.privatelink.azconfig.io"));
            Assert.True(configClientManager.IsValidEndpoint("azure-replica.azconfig.io"));
            Assert.False(configClientManager.IsValidEndpoint("azure.badazconfig.io"));
            Assert.False(configClientManager.IsValidEndpoint("azure.azconfigbad.io"));
            Assert.False(configClientManager.IsValidEndpoint("azure.appconfig.azure.com"));
            Assert.False(configClientManager.IsValidEndpoint("azure.azconfig.bad.io"));

            var configClientManager2 = new ConfigurationClientManager(
                new[] { new Uri("https://foobar.appconfig.azure.com") },
                new DefaultAzureCredential(),
                new ConfigurationClientOptions(),
                true);

            Assert.True(configClientManager2.IsValidEndpoint("azure.appconfig.azure.com"));
            Assert.True(configClientManager2.IsValidEndpoint("azure.z1.appconfig.azure.com"));
            Assert.True(configClientManager2.IsValidEndpoint("azure-replia.z1.appconfig.azure.com"));
            Assert.True(configClientManager2.IsValidEndpoint("azure.privatelink.appconfig.azure.com"));
            Assert.True(configClientManager2.IsValidEndpoint("azconfig.appconfig.azure.com"));
            Assert.False(configClientManager2.IsValidEndpoint("azure.azconfig.io"));
            Assert.False(configClientManager2.IsValidEndpoint("azure.badappconfig.azure.com"));
            Assert.False(configClientManager2.IsValidEndpoint("azure.appconfigbad.azure.com"));

            var configClientManager3 = new ConfigurationClientManager(
                new[] { new Uri("https://foobar.azconfig-test.io") },
                new DefaultAzureCredential(),
                new ConfigurationClientOptions(),
                true);

            Assert.False(configClientManager3.IsValidEndpoint("azure.azconfig-test.io"));
            Assert.False(configClientManager3.IsValidEndpoint("azure.azconfig.io"));

            var configClientManager4 = new ConfigurationClientManager(
                new[] { new Uri("https://foobar.z1.appconfig-test.azure.com") },
                new DefaultAzureCredential(),
                new ConfigurationClientOptions(),
                true);

            Assert.False(configClientManager4.IsValidEndpoint("foobar.z2.appconfig-test.azure.com"));
            Assert.False(configClientManager4.IsValidEndpoint("foobar.appconfig-test.azure.com"));
            Assert.False(configClientManager4.IsValidEndpoint("foobar.appconfig.azure.com"));
        }

        [Fact]
        public void FailOverTests_GetNoDynamicClient()
        {
            var configClientManager = new ConfigurationClientManager(
                new[] { new Uri("https://azure.azconfig.io") },
                new DefaultAzureCredential(),
                new ConfigurationClientOptions(),
                true);

            var clients = configClientManager.GetClients();

            // Only contains the client that passed while constructing the ConfigurationClientManager
            Assert.Single(clients);
        }
    }
}
