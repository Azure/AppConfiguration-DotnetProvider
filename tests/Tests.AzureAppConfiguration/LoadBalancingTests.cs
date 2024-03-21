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
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class LoadBalancingTests
    {
        readonly ConfigurationSetting kv = ConfigurationModelFactory.ConfigurationSetting(key: "TestKey1", label: "label", value: "TestValue1",
                                                                                  eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                                                                                  contentType: "text");

        TimeSpan CacheExpirationTime = TimeSpan.FromSeconds(1);

        [Fact]
        public void LoadBalancingTests_UsesAllEndpoints()
        {
            IConfigurationRefresher refresher = null;
            var mockResponse = new MockResponse(200);

            var mockClient1 = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient1.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Response.FromValue<ConfigurationSetting>(kv, mockResponse));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Response.FromValue(kv, mockResponse));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            var mockClient2 = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient2.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Response.FromValue<ConfigurationSetting>(kv, mockResponse));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Response.FromValue(kv, mockResponse));
            mockClient2.Setup(c => c.Equals(mockClient2)).Returns(true);

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, mockClient1.Object);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var clientList = new List<ConfigurationClientWrapper>() { cw1, cw2 };
            var configClientManager = new ConfigurationClientManager(clientList);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = configClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });
                    options.ReplicaDiscoveryEnabled = false;
                    options.LoadBalancingEnabled = true;

                    refresher = options.GetRefresher();
                }).Build();

            // Ensure client 1 was used for startup
            mockClient1.Verify(mc => mc.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Exactly(1));

            Thread.Sleep(CacheExpirationTime);
            refresher.RefreshAsync().Wait();

            // Ensure client 2 was used for refresh
            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(0));

            mockClient2.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(1));

            Thread.Sleep(CacheExpirationTime);
            refresher.RefreshAsync().Wait();

            // Ensure client 1 was now used for refresh
            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
        }

        [Fact]
        public void LoadBalancingTests_UsesClientAfterBackoffEnds()
        {
            IConfigurationRefresher refresher = null;
            var mockResponse = new MockResponse(200);

            var mockClient1 = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient1.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Response.FromValue<ConfigurationSetting>(kv, mockResponse));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Response.FromValue(kv, mockResponse));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            var mockClient2 = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient2.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Response.FromValue<ConfigurationSetting>(kv, mockResponse));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Response.FromValue(kv, mockResponse));
            mockClient2.Setup(c => c.Equals(mockClient2)).Returns(true);

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, mockClient1.Object);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var clientList = new List<ConfigurationClientWrapper>() { cw1, cw2 };
            var configClientManager = new ConfigurationClientManager(clientList);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.MinBackoffDuration = TimeSpan.FromSeconds(2);
                    options.ClientManager = configClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });
                    options.ReplicaDiscoveryEnabled = false;
                    options.LoadBalancingEnabled = true;

                    refresher = options.GetRefresher();
                }).Build();

            // Ensure client 2 was used for startup
            mockClient2.Verify(mc => mc.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Exactly(1));

            Thread.Sleep(TimeSpan.FromSeconds(2));
            refresher.RefreshAsync().Wait();

            // Ensure client 1 has recovered and is used for refresh
            mockClient2.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(0));

            mockClient1.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(1));

            Thread.Sleep(CacheExpirationTime);
            refresher.RefreshAsync().Wait();

            mockClient2.Verify(mc => mc.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
        }
    }
}
