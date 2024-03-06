// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Diagnostics;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class LoadBalancingTests
    {
        readonly ConfigurationSetting _kv = ConfigurationModelFactory.ConfigurationSetting(key: "TestKey1", label: "label", value: "TestValue1",
                                                                                  eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                                                                                  contentType: "text");

        TimeSpan CacheExpirationTime = TimeSpan.FromSeconds(1);

        [Fact]
        public void LoadBalancingTests_UsesAllClients()
        {
            // Arrange
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();

            var mockClient1 = GetMockConfigurationClient();
            var mockClient2 = GetMockConfigurationClient();

            ConfigurationClientWrapper cw1 = new ConfigurationClientWrapper(TestHelpers.PrimaryConfigStoreEndpoint, mockClient1.Object);
            ConfigurationClientWrapper cw2 = new ConfigurationClientWrapper(TestHelpers.SecondaryConfigStoreEndpoint, mockClient2.Object);

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient1.Object, mockClient2.Object);

            string invocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Verbose)
                    {
                        invocation += s;
                    }
                }, EventLevel.Verbose);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.Select("TestKey*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });

                    options.LoadBalancingEnabled = true;

                    refresher = options.GetRefresher();
                }).Build();

            _kv.Value = "newValue1";

            Thread.Sleep(CacheExpirationTime);
            refresher.TryRefreshAsync().Wait();

            _kv.Value = "newValue2";

            Thread.Sleep(CacheExpirationTime);
            refresher.TryRefreshAsync().Wait();

            // Regardless of order, both endpoints should have been used during refresh with load balancing
            Assert.Contains(LogHelper.BuildKeyValueReadMessage(KeyValueChangeType.Modified, _kv.Key, _kv.Label, TestHelpers.SecondaryConfigStoreEndpoint.ToString().TrimEnd('/')), invocation);
            Assert.Contains(LogHelper.BuildKeyValueReadMessage(KeyValueChangeType.Modified, _kv.Key, _kv.Label, TestHelpers.PrimaryConfigStoreEndpoint.ToString().TrimEnd('/')), invocation);
        }

        private Mock<ConfigurationClient> GetMockConfigurationClient()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return Response.FromValue(TestHelpers.CloneSetting(_kv), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var newSetting = _kv;
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            // We don't actually select KV based on SettingSelector, we just return a deep copy of _kvCollection
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    return new MockAsyncPageable(new List<ConfigurationSetting> { _kv }.ToList());
                });

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            return mockClient;
        }
    }
}
