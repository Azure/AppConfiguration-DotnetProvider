// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement; // Added for feature flag constants
using Moq;
using System;
using System.Collections.Generic;
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
        public void AfdTests_WatchedSettingIsUnsupportedWhenConnectAzureFrontDoor()
        {
            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", true);
                    });
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("Registering individual keys for refresh via `AzureAppConfigurationRefreshOptions.Register` is not supported when connecting to Azure Front Door.", exception.InnerException.Message);
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
                .Returns(mockAsyncPageable1)  // initial load
                .Returns(mockAsyncPageable3); // reload after change detected

            mockClient.SetupSequence(c => c.CheckConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable2)  // first check - stale, should not refresh
                .Returns(mockAsyncPageable3); // second check - should trigger refresh

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

        [Fact]
        public async Task AfdTests_FeatureFlagsRefresh()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            var featureFlag = new List<ConfigurationSetting>
            {
                ConfigurationModelFactory.ConfigurationSetting(
                    key: FeatureManagementConstants.FeatureFlagMarker + "BetaFlag",
                    value: @"
                    {
                      ""id"": ""BetaFlag"",
                      ""enabled"": true,
                      ""conditions"": {
                        ""client_filters"": [
                          {
                            ""name"": ""Browser"",
                            ""parameters"": {
                              ""AllowedBrowsers"": [ ""Firefox"", ""Safari"" ]
                            }
                          }
                        ]
                      }
                    }",
                    label: default,
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                    eTag: new ETag(Guid.NewGuid().ToString()))
            };

            var staleFeatureFlag = new List<ConfigurationSetting>
            {
                ConfigurationModelFactory.ConfigurationSetting(
                    key: FeatureManagementConstants.FeatureFlagMarker + "BetaFlag",
                    value: @"
                    {
                      ""id"": ""BetaFlag"",
                      ""enabled"": true,
                      ""conditions"": {
                        ""client_filters"": [
                          {
                            ""name"": ""Browser"",
                            ""parameters"": {
                              ""AllowedBrowsers"": [ ""360"" ]
                            }
                          }
                        ]
                      }
                    }",
                    label: default,
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                    eTag: new ETag(Guid.NewGuid().ToString()))
            };

            var newFeatureFlag = new List<ConfigurationSetting>
            {
                ConfigurationModelFactory.ConfigurationSetting(
                    key: FeatureManagementConstants.FeatureFlagMarker + "BetaFlag",
                    value: @"
                    {
                      ""id"": ""BetaFlag"",
                      ""enabled"": true,
                      ""conditions"": {
                        ""client_filters"": [
                          {
                            ""name"": ""Browser"",
                            ""parameters"": {
                              ""AllowedBrowsers"": [ ""Chrome"", ""Edge"" ]
                            }
                          }
                        ]
                      }
                    }",
                    label: default,
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                    eTag: new ETag(Guid.NewGuid().ToString()))
            };

            string etag1 = Guid.NewGuid().ToString();
            var responses = new List<MockResponse>()
            {
                new MockResponse(200, etag1, DateTimeOffset.Parse("2025-10-17T09:00:00+08:00"))
            };
            var mockAsyncPageable1 = new MockAsyncPageable(featureFlag, null, 10, responses);

            string etag2 = Guid.NewGuid().ToString();
            var responses2 = new List<MockResponse>()
            {
                new MockResponse(200, etag2, DateTimeOffset.Parse("2025-10-17T08:59:59+08:00"))
            };
            var mockAsyncPageable2 = new MockAsyncPageable(staleFeatureFlag, null, 10, responses);

            string etag3 = Guid.NewGuid().ToString();
            var responses3 = new List<MockResponse>()
            {
                new MockResponse(200, etag3, DateTimeOffset.Parse("2025-10-17T09:00:02+08:00"))
            };
            var mockAsyncPageable3 = new MockAsyncPageable(newFeatureFlag, null, 10, responses3);

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable1)  // default load configuration settings 
                .Returns(mockAsyncPageable1)  // load feature flag
                .Returns(mockAsyncPageable3)  // reload after change detected
                .Returns(mockAsyncPageable3); // reload feature flags

            mockClient.SetupSequence(c => c.CheckConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(mockAsyncPageable2)  // watch request, should not trigger refresh
                .Returns(mockAsyncPageable3); // watch request, should trigger refresh

            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            IConfigurationRefresher refresher = null;
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigurationSettingPageIterator = new MockConfigurationSettingPageIterator();
                    options.UseFeatureFlags(o => o.SetRefreshInterval(TimeSpan.FromSeconds(1)));
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Browser", config["FeatureManagement:BetaFlag:EnabledFor:0:Name"]);
            Assert.Equal("Firefox", config["FeatureManagement:BetaFlag:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:BetaFlag:EnabledFor:0:Parameters:AllowedBrowsers:1"]);

            await Task.Delay(1500);

            await refresher.RefreshAsync();

            // Still old values because page timestamp was stale
            Assert.Equal("Firefox", config["FeatureManagement:BetaFlag:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:BetaFlag:EnabledFor:0:Parameters:AllowedBrowsers:1"]);

            await Task.Delay(1500);

            await refresher.RefreshAsync();

            Assert.Equal("Chrome", config["FeatureManagement:BetaFlag:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["FeatureManagement:BetaFlag:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
        }
    }
}
