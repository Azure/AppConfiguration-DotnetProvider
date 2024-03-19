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
    public class LoadBalancingTests
    {
        readonly ConfigurationSetting kv1 = ConfigurationModelFactory.ConfigurationSetting(key: "TestKey1", label: "label", value: "TestValue1",
                                                                                  eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                                                                                  contentType: "text");

        readonly ConfigurationSetting kv2 = ConfigurationModelFactory.ConfigurationSetting(key: "TestKey2", label: "label", value: "TestValue2",
                                                                                  eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e64"),
                                                                                  contentType: "text");

        TimeSpan CacheExpirationTime = TimeSpan.FromSeconds(1);

        [Fact]
        public void LoadBalancingTests_UsesAllEndpoints()
        {
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();

            var mockClient1 = new Mock<ConfigurationClient>();
            mockClient1.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv1, mockResponse.Object)));
            mockClient1.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv1, mockResponse.Object)));
            mockClient1.Setup(c => c.Equals(mockClient1)).Returns(true);

            var mockClient2 = new Mock<ConfigurationClient>();
            mockClient2.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv2, mockResponse.Object)));
            mockClient2.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.FromResult(Response.FromValue<ConfigurationSetting>(kv2, mockResponse.Object)));
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
                        refreshOptions.Register("TestKey2", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });
                    options.ReplicaDiscoveryEnabled = false;

                    refresher = options.GetRefresher();
                }).Build();

            Thread.Sleep(CacheExpirationTime);
            refresher.TryRefreshAsync().Wait();

            Assert.Null(config["TestKey1"]);
        }
    }
}
