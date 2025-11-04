// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Data.AppConfiguration.Tests;
using Azure.Identity;
using Azure.ResourceManager.Resources.Models;
using Microsoft.Extensions.Azure;
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
    public class AfdTests
    {
        List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting("TestKey1", "TestValue1", "label",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType:"text"),
            ConfigurationModelFactory.ConfigurationSetting("TestKey2", "TestValue2", "label",
                eTag: new ETag("31c38369-831f-4bf1-b9ad-79db56c8b989"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("TestKey3", "TestValue3", "label",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("TestKey4", "TestValue4", "label",
                eTag: new ETag("3ca43b3e-d544-4b0c-b3a2-e7a7284217a2"),
                contentType: "text")
        };

        private class TestClientFactory : IAzureClientFactory<ConfigurationClient>
        {
            public ConfigurationClient CreateClient(string name)
            {
                throw new NotImplementedException();
            }
        }

        [Fact]
        public void AfdTests_ConnectThrowsAfterConnectAzureFrontDoor()
        {
            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            var endpoint = new Uri("https://fake-endpoint.azconfig.io");
            var connectionString = "Endpoint=https://fake-endpoint.azconfig.io;Id=test;Secret=123456";
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.Connect(endpoint, new DefaultAzureCredential());
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.ConnectionConflict, exception.InnerException.Message);

            exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.Connect(connectionString);
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.ConnectionConflict, exception.InnerException.Message);
        }

        [Fact]
        public void AfdTests_ConnectAzureFrontDoorThrowsAfterConnect()
        {
            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            var endpoint = new Uri("https://fake-endpoint.azconfig.io");
            var connectionString = "Endpoint=https://fake-endpoint.azconfig.io;Id=test;Secret=123456";
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.Connect(endpoint, new DefaultAzureCredential());
                    options.ConnectAzureFrontDoor(afdEndpoint);
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.ConnectionConflict, exception.InnerException.Message);

            exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.Connect(connectionString);
                    options.ConnectAzureFrontDoor(afdEndpoint);
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.ConnectionConflict, exception.InnerException.Message);
        }

        [Fact]
        public void AfdTests_ThrowsWhenConnectMultipleAzureFrontDoor()
        {
            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            var afdEndpoint2 = new Uri("https://test.b02.azurefd.net");
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.ConnectAzureFrontDoor(afdEndpoint2);
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.AfdConnectionConflict, exception.InnerException.Message);
        }

        [Fact]
        public void AfdTests_LoadbalancingIsUnsupportedWhenConnectAzureFrontDoor()
        {
            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.LoadBalancingEnabled = true;
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.AfdLoadBalancingUnsupported, exception.InnerException.Message);
        }

        [Fact]
        public void AfdTests_CustomClientOptionsNotSupported()
        {
            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.SetClientFactory(new TestClientFactory());
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.AfdCustomClientFactoryUnsupported, exception.InnerException.Message);
        }

        [Fact]
        public async Task AfdTests_RefreshSingleWatchedSetting()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            var keyValueCollection = new List<ConfigurationSetting>(_kvCollection);
            var mockAsyncPageable = new MockAsyncPageable(keyValueCollection);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable);

            string etag1 = Guid.NewGuid().ToString();
            var setting = ConfigurationModelFactory.ConfigurationSetting(
                "Sentinel",
                "sentinel-value",
                "label",
                eTag: new ETag(etag1),
                contentType: "text");

            string etag2 = Guid.NewGuid().ToString();
            var oldSetting = ConfigurationModelFactory.ConfigurationSetting(
                "Sentinel",
                "old-value",
                "label",
                eTag: new ETag(etag2),
                contentType: "text");

            string etag3 = Guid.NewGuid().ToString();
            var newSetting = ConfigurationModelFactory.ConfigurationSetting(
                "Sentinel",
                "new-value",
                "label",
                eTag: new ETag(etag3),
                contentType: "text");

            mockClient.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(setting, new MockResponse(200, etag1, DateTimeOffset.Parse("2025-10-17T09:00:00+08:00"))));

            mockClient.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(oldSetting, new MockResponse(200, etag2, DateTimeOffset.Parse("2025-10-17T08:59:59+08:00")))) // stale
                .ReturnsAsync(Response.FromValue(newSetting, new MockResponse(200, etag3, DateTimeOffset.Parse("2025-10-17T09:00:03+08:00")))); // up-to-date

            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            IConfigurationRefresher refresher = null;
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select("TestKey*", "label");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("Sentinel", "label", false)
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("sentinel-value", config["Sentinel"]);

            await Task.Delay(1500);

            await refresher.RefreshAsync();

            Assert.Equal("sentinel-value", config["Sentinel"]); // should not refresh, because the response is out of date

            await Task.Delay(1500);

            await refresher.RefreshAsync();

            Assert.Equal("new-value", config["Sentinel"]);
        }

        [Fact]
        public async Task AfdTests_WatchedSettingRefreshAll()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            var keyValueCollection1 = new List<ConfigurationSetting>(_kvCollection);
            string page1_etag = Guid.NewGuid().ToString();
            string page2_etag = Guid.NewGuid().ToString();
            var responses = new List<MockResponse>()
            {
                new MockResponse(200, page1_etag, DateTimeOffset.Parse("2025-10-17T09:00:00+08:00")),
                new MockResponse(200, page2_etag, DateTimeOffset.Parse("2025-10-17T09:00:00+08:00"))
            };
            var mockAsyncPageable1 = new MockAsyncPageable(keyValueCollection1, null, 3, responses);

            var keyValueCollection2 = new List<ConfigurationSetting>(_kvCollection);
            keyValueCollection2[3].Value = "updated-value";
            string page2_etag2 = Guid.NewGuid().ToString();
            var responses2 = new List<MockResponse>()
            {
                new MockResponse(200, page1_etag, DateTimeOffset.Parse("2025-10-17T09:00:01+08:00")),
                new MockResponse(200, page2_etag2, DateTimeOffset.Parse("2025-10-17T09:00:01+08:00"))
            };
            var mockAsyncPageable2 = new MockAsyncPageable(keyValueCollection2, null, 3, responses2);

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable1)
                .Returns(mockAsyncPageable2);

            string etag1 = Guid.NewGuid().ToString();
            var setting = ConfigurationModelFactory.ConfigurationSetting(
                "Sentinel",
                "sentinel-value",
                "label",
                eTag: new ETag(etag1),
                contentType: "text");

            string etag2 = Guid.NewGuid().ToString();
            var oldSetting = ConfigurationModelFactory.ConfigurationSetting(
                "Sentinel",
                "old-value",
                "label",
                eTag: new ETag(etag2),
                contentType: "text");

            string etag3 = Guid.NewGuid().ToString();
            var newSetting = ConfigurationModelFactory.ConfigurationSetting(
                "Sentinel",
                "new-value",
                "label",
                eTag: new ETag(etag3),
                contentType: "text");

            mockClient.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(setting, new MockResponse(200, etag1, DateTimeOffset.Parse("2025-10-17T09:00:00+08:00"))))
                .ReturnsAsync(Response.FromValue(newSetting, new MockResponse(200, etag3, DateTimeOffset.Parse("2025-10-17T09:00:01+08:00"))));

            mockClient.SetupSequence(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(oldSetting, new MockResponse(200, etag2, DateTimeOffset.Parse("2025-10-17T08:59:59+08:00")))) // stale, should not refresh
                .ReturnsAsync(Response.FromValue(newSetting, new MockResponse(200, etag3, DateTimeOffset.Parse("2025-10-17T09:00:01+08:00"))));

            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            IConfigurationRefresher refresher = null;
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select("TestKey*", "label");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("Sentinel", "label", true)
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("sentinel-value", config["Sentinel"]);

            await Task.Delay(1500);

            await refresher.RefreshAsync();

            Assert.Equal("TestValue4", config["TestKey4"]); // should not refresh, because sentinel is stale
            Assert.Equal("sentinel-value", config["Sentinel"]);

            await Task.Delay(1500);

            await refresher.RefreshAsync();

            Assert.Equal("updated-value", config["TestKey4"]);
            Assert.Equal("new-value", config["Sentinel"]);
        }

        [Fact]
        public async Task AfdTests_RegisterAllRefresh()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            var keyValueCollection1 = new List<ConfigurationSetting>(_kvCollection);
            string page1_etag = Guid.NewGuid().ToString();
            string page2_etag = Guid.NewGuid().ToString();
            var responses = new List<MockResponse>()
            {
                new MockResponse(200, page1_etag, DateTimeOffset.Parse("2025-10-17T09:00:00+08:00")),
                new MockResponse(200, page2_etag, DateTimeOffset.Parse("2025-10-17T09:00:00+08:00"))
            };
            var mockAsyncPageable1 = new MockAsyncPageable(keyValueCollection1, null, 3, responses);

            var keyValueCollection2 = new List<ConfigurationSetting>(_kvCollection);
            keyValueCollection2[3].Value = "old-value";
            string page2_etag2 = Guid.NewGuid().ToString();
            var responses2 = new List<MockResponse>()
            {
                new MockResponse(200, page1_etag, DateTimeOffset.Parse("2025-10-17T09:00:00+08:00")),
                new MockResponse(200, page2_etag2, DateTimeOffset.Parse("2025-10-17T08:59:59+08:00")) // stale, should not refresh
            };
            var mockAsyncPageable2 = new MockAsyncPageable(keyValueCollection2, null, 3, responses2);

            var keyValueCollection3 = new List<ConfigurationSetting>(_kvCollection);
            keyValueCollection3[3].Value = "new-value";
            var responses3 = new List<MockResponse>()
            {
                new MockResponse(200, page1_etag, DateTimeOffset.Parse("2025-10-17T09:00:00+08:00")), // up-to-date, should refresh
                new MockResponse(200, page2_etag2, DateTimeOffset.Parse("2025-10-17T09:00:02+08:00"))
            };
            var mockAsyncPageable3 = new MockAsyncPageable(keyValueCollection3, null, 3, responses3);

            var responses4 = new List<MockResponse>()
            {
                new MockResponse(200, page1_etag, DateTimeOffset.Parse("2025-10-17T09:00:03+08:00")), // up-to-date
                new MockResponse(200, page2_etag2, DateTimeOffset.Parse("2025-10-17T09:00:03+08:00"))
            };
            var mockAsyncPageable4 = new MockAsyncPageable(keyValueCollection3, null, 3, responses4);

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable1)
                .Returns(mockAsyncPageable2)
                .Returns(mockAsyncPageable3)
                .Returns(mockAsyncPageable4);

            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            IConfigurationRefresher refresher = null;
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigurationSettingPageIterator = new MockConfigurationSettingPageIterator();
                    options.Select("TestKey*", "label");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.RegisterAll()
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue4", config["TestKey4"]);

            await Task.Delay(1500);

            await refresher.RefreshAsync();

            Assert.Equal("TestValue4", config["TestKey4"]); // should not refresh, because page 2 is stale

            await Task.Delay(1500);

            await refresher.RefreshAsync();

            Assert.Equal("new-value", config["TestKey4"]);
        }
    }
}
