// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core;
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
    public class FailOverTests
    {
        readonly ConfigurationSetting kv = ConfigurationModelFactory.ConfigurationSetting(key: "TestKey1", label: "label", value: "TestValue1",
                                                                                          eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                                                                                          contentType: "text");

        [Fact]
        public async Task FailOverTests_ReturnsAllClientsIfAllBackedOff()
        {
            // Arrange
            IConfigurationRefresher refresher = null;

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
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
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

            await refresher.RefreshAsync();

            // The client manager should have called RefreshClients when all clients were backed off
            Assert.Equal(1, configClientManager.RefreshClientsCalled);
        }

        [Fact]
        public void FailOverTests_PropagatesNonFailOverableExceptions()
        {
            // Arrange
            IConfigurationRefresher refresher = null;

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
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                });

            // Throws last exception when all clients fail.
            Assert.Throws<RequestFailedException>(configBuilder.Build);
        }

        [Fact]
        public async Task FailOverTests_BackoffStateIsUpdatedOnSuccessfulRequest()
        {
            // Arrange
            IConfigurationRefresher refresher = null;
            var mockResponse = new MockResponse(200);

            var mockClient1 = new Mock<ConfigurationClient>();
            mockClient1.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient1.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)));
            mockClient1.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            var mockClient2 = new Mock<ConfigurationClient>();
            mockClient2.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient2.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)));
            mockClient2.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)));
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
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                }).Build();

            await refresher.RefreshAsync();

            // The first client should not have been called during refresh
            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(0));

            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(0));

            mockClient2.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(1));

            // Wait for client 1 backoff to end
            Thread.Sleep(2500);

            await refresher.RefreshAsync();

            // The first client should have been called now with refresh after the backoff time ends
            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
        }

        [Fact]
        public void FailOverTests_AutoFailover()
        {
            // Arrange
            IConfigurationRefresher refresher = null;
            var mockResponse = new MockResponse(200);

            var mockClient1 = new Mock<ConfigurationClient>();
            mockClient1.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(403, "Forbidden."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(401, "Unauthorized."));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            var mockClient2 = new Mock<ConfigurationClient>();
            mockClient2.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)));
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
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();
        }

        [Fact]
        public void FailOverTests_ValidateEndpoints()
        {
            var clientFactory = new AzureAppConfigurationClientFactory(new DefaultAzureCredential(), new ConfigurationClientOptions());

            var configClientManager = new ConfigurationClientManager(
                clientFactory,
                new[] { new Uri("https://foobar.azconfig.io") },
                true,
                false);

            Assert.True(configClientManager.IsValidEndpoint("azure.azconfig.io"));
            Assert.True(configClientManager.IsValidEndpoint("appconfig.azconfig.io"));
            Assert.True(configClientManager.IsValidEndpoint("azure.privatelink.azconfig.io"));
            Assert.True(configClientManager.IsValidEndpoint("azure-replica.azconfig.io"));
            Assert.False(configClientManager.IsValidEndpoint("azure.badazconfig.io"));
            Assert.False(configClientManager.IsValidEndpoint("azure.azconfigbad.io"));
            Assert.False(configClientManager.IsValidEndpoint("azure.appconfig.azure.com"));
            Assert.False(configClientManager.IsValidEndpoint("azure.azconfig.bad.io"));

            var configClientManager2 = new ConfigurationClientManager(
                clientFactory,
                new[] { new Uri("https://foobar.appconfig.azure.com") },
                true,
                false);

            Assert.True(configClientManager2.IsValidEndpoint("azure.appconfig.azure.com"));
            Assert.True(configClientManager2.IsValidEndpoint("azure.z1.appconfig.azure.com"));
            Assert.True(configClientManager2.IsValidEndpoint("azure-replia.z1.appconfig.azure.com"));
            Assert.True(configClientManager2.IsValidEndpoint("azure.privatelink.appconfig.azure.com"));
            Assert.True(configClientManager2.IsValidEndpoint("azconfig.appconfig.azure.com"));
            Assert.False(configClientManager2.IsValidEndpoint("azure.azconfig.io"));
            Assert.False(configClientManager2.IsValidEndpoint("azure.badappconfig.azure.com"));
            Assert.False(configClientManager2.IsValidEndpoint("azure.appconfigbad.azure.com"));

            var configClientManager3 = new ConfigurationClientManager(
                clientFactory,
                new[] { new Uri("https://foobar.azconfig-test.io") },
                true,
                false);

            Assert.False(configClientManager3.IsValidEndpoint("azure.azconfig-test.io"));
            Assert.False(configClientManager3.IsValidEndpoint("azure.azconfig.io"));

            var configClientManager4 = new ConfigurationClientManager(
                clientFactory,
                new[] { new Uri("https://foobar.z1.appconfig-test.azure.com") },
                true,
                false);

            Assert.False(configClientManager4.IsValidEndpoint("foobar.z2.appconfig-test.azure.com"));
            Assert.False(configClientManager4.IsValidEndpoint("foobar.appconfig-test.azure.com"));
            Assert.False(configClientManager4.IsValidEndpoint("foobar.appconfig.azure.com"));
        }

        [Fact]
        public void FailOverTests_GetNoDynamicClient()
        {
            var clientFactory = new AzureAppConfigurationClientFactory(new DefaultAzureCredential(), new ConfigurationClientOptions());

            var configClientManager = new ConfigurationClientManager(
                clientFactory,
                new[] { new Uri("https://azure.azconfig.io") },
                true,
                false);

            var clients = configClientManager.GetClients();

            // Only contains the client that passed while constructing the ConfigurationClientManager
            Assert.Single(clients);
        }

        [Fact]
        public void FailOverTests_NetworkTimeout()
        {
            var mockResponse = new MockResponse(200);

            var client1 = new ConfigurationClient(TestHelpers.CreateMockEndpointString(),
                new ConfigurationClientOptions()
                {
                    Retry =
                    {
                        NetworkTimeout = TimeSpan.FromTicks(1)
                    }
                });

            var mockClient2 = new Mock<ConfigurationClient>();
            mockClient2.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)));
            mockClient2.Setup(c => c.Equals(mockClient2)).Returns(true);

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, client1);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var clientList = new List<ConfigurationClientWrapper>() { cw1 };
            var autoFailoverList = new List<ConfigurationClientWrapper>() { cw2 };
            var configClientManager = new MockedConfigurationClientManager(clientList, autoFailoverList);

            // Make sure the provider fails over and will load correctly using the second client
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = configClientManager;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                })
                .Build();

            // Make sure the provider fails on startup and throws the expected exception due to startup timeout
            Exception exception = Assert.Throws<TimeoutException>(() =>
            {
                config = new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(TestHelpers.CreateMockEndpointString());
                        options.Select("TestKey*");
                        options.ConfigureRefresh(refreshOptions =>
                        {
                            refreshOptions.Register("TestKey1", "label")
                                .SetRefreshInterval(TimeSpan.FromSeconds(1));
                        });
                        options.ConfigureStartupOptions(startup =>
                        {
                            startup.Timeout = TimeSpan.FromSeconds(5);
                        });
                        options.ConfigureClientOptions(clientOptions =>
                        {
                            clientOptions.Retry.NetworkTimeout = TimeSpan.FromTicks(1);
                        });
                    })
                    .Build();
            });

            // Make sure the startup exception is due to network timeout
            // Aggregate exception is nested due to how provider stores all startup exceptions thrown
            Assert.True(exception.InnerException is AggregateException ae &&
                ae.InnerException is AggregateException ae2 &&
                ae2.InnerExceptions.All(ex => ex is TaskCanceledException) &&
                ae2.InnerException is TaskCanceledException tce);
        }

        [Fact]
        public async Task FailOverTests_AllClientsBackedOffAfterNonFailoverableException()
        {
            IConfigurationRefresher refresher = null;
            var mockResponse = new MockResponse(200);

            // Setup first client - succeeds on startup, fails with 404 (non-failoverable) on first refresh
            var mockClient1 = new Mock<ConfigurationClient>();
            mockClient1.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()))
                       .Throws(new RequestFailedException(412, "Request failed."))
                       .Throws(new RequestFailedException(412, "Request failed."));
            mockClient1.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)))
                        .Throws(new RequestFailedException(412, "Request failed."))
                        .Throws(new RequestFailedException(412, "Request failed."));
            mockClient1.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(412, "Request failed."))
                       .Throws(new RequestFailedException(412, "Request failed."));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            // Setup second client - succeeds on startup, should not be called during refresh
            var mockClient2 = new Mock<ConfigurationClient>();
            mockClient2.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv, mockResponse)));
            mockClient2.Setup(c => c.Equals(mockClient2)).Returns(true);

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, mockClient1.Object);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var clientList = new List<ConfigurationClientWrapper>() { cw1, cw2 };
            var configClientManager = new ConfigurationClientManager(clientList);

            // Verify 2 clients are available
            Assert.Equal(2, configClientManager.GetClients().Count());

            // Act & Assert - Build configuration successfully with both clients
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = configClientManager;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    options.ReplicaDiscoveryEnabled = false;
                    refresher = options.GetRefresher();
                }).Build();

            // First refresh - should call client 1 and fail with non-failoverable exception
            // This should cause all clients to be backed off
            await Task.Delay(1500);
            await refresher.TryRefreshAsync();

            // Verify that client 1 was called during the first refresh
            mockClient1.Verify(mc => mc.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(1));

            // Verify that client 2 was not called during the first refresh
            mockClient2.Verify(mc => mc.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient2.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient2.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

            // Second refresh - no clients should be called as all are backed off
            await Task.Delay(1500);
            await refresher.TryRefreshAsync();

            // Verify that no additional calls were made to any client during the second refresh
            mockClient1.Verify(mc => mc.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
            mockClient2.Verify(mc => mc.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient2.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            mockClient2.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
