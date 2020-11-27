﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Data.AppConfiguration.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class FeatureManagementTests
    {
        private ConfigurationSetting _kv = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "myFeature",
            value: @"
                    {
                      ""id"": ""Beta"",
                      ""description"": ""The new beta version of our web site."",
                      ""display_name"": ""Beta Feature"",
                      ""enabled"": true,
                      ""conditions"": {
                        ""client_filters"": [
                          {
                            ""name"": ""Browser"",
                            ""parameters"": {
                              ""AllowedBrowsers"": [ ""Firefox"", ""Safari"" ]
                            }
                          },
                          {
                            ""name"": ""RollOut"",
                            ""parameters"": {
                              ""percentage"": ""20"",
                              ""region"": ""US""
                            }
                          },
                          {
                            ""name"": ""SuperUsers""
                          },
                          {
                            ""name"": ""TimeWindow"",
                            ""parameters"": {
	                          ""Start"": ""\/Date(1578643200000)\/"",
	                          ""End"": ""\/Date(1578686400000)\/""
                            }
                          }
                        ]
                      }
                    }
                    ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"));

        private ConfigurationSetting _kv2 = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "myFeature2",
            value: @"
                    {
                      ""id"": ""MyFeature2"",
                      ""description"": ""The new beta version of our web site."",
                      ""display_name"": ""Beta Feature"",
                      ""enabled"": true,
                      ""conditions"": {
                        ""client_filters"": [
                          {
                            ""name"": ""SuperUsers""
                          }
                        ]
                      }
                    }
                    ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"));

        [Fact]
        public void UsesFeatureFlags()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            var featureFlags = new List<ConfigurationSetting> { _kv };

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(
                It.Is<SettingSelector>(settingsSelector => settingsSelector.KeyFilter == "*"),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var testClient = mockClient.Object;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = testClient;
                    options.UseFeatureFlags();
                })
                .Build();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Firefox", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["FeatureManagement:Beta:EnabledFor:1:Name"]);
            Assert.Equal("20", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Percentage"]);
            Assert.Equal("US", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Region"]);
            Assert.Equal("SuperUsers", config["FeatureManagement:Beta:EnabledFor:2:Name"]);
            Assert.Equal("TimeWindow", config["FeatureManagement:Beta:EnabledFor:3:Name"]);
            Assert.Equal("/Date(1578643200000)/", config["FeatureManagement:Beta:EnabledFor:3:Parameters:Start"]);
            Assert.Equal("/Date(1578686400000)/", config["FeatureManagement:Beta:EnabledFor:3:Parameters:End"]);
        }

        [Fact]
        public void UsesOnlySelectedFeatureFlags()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(
                    It.Is<SettingSelector>(settingsSelector => settingsSelector.KeyFilter == ".appconfig.featureflag/Beta"),
                    It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(
                    It.Is<SettingSelector>(settingsSelector => settingsSelector.KeyFilter == ".appconfig.featureflag/MyFeature2"),
                    It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv2 }));

            var testClient = mockClient.Object;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = testClient;
                    options.UseFeatureFlags(options =>
                    {
                        options.Select("Beta");
                        options.Select("MyFeature2");
                    });
                })
                .Build();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);

            Assert.Equal("SuperUsers", config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
        }

        [Fact]
        public void WatchesFeatureFlags()
        {
            var featureFlags = new List<ConfigurationSetting> { _kv };

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            IConfigurationRefresher refresher = null;
            var cacheExpirationTimeSpan = TimeSpan.FromSeconds(1);
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.UseFeatureFlags(o => o.CacheExpirationInterval = cacheExpirationTimeSpan);

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Firefox", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["FeatureManagement:Beta:EnabledFor:1:Name"]);
            Assert.Equal("20", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Percentage"]);
            Assert.Equal("US", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Region"]);
            Assert.Equal("SuperUsers", config["FeatureManagement:Beta:EnabledFor:2:Name"]);
            Assert.Equal("TimeWindow", config["FeatureManagement:Beta:EnabledFor:3:Name"]);
            Assert.Equal("/Date(1578643200000)/", config["FeatureManagement:Beta:EnabledFor:3:Parameters:Start"]);
            Assert.Equal("/Date(1578686400000)/", config["FeatureManagement:Beta:EnabledFor:3:Parameters:End"]);

            featureFlags[0] = ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "myFeature",
                value: @"
                        {
                          ""id"": ""Beta"",
                          ""description"": ""The new beta version of our web site."",
                          ""display_name"": ""Beta Feature"",
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
                        }
                        ",
                label: default,
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            featureFlags.Add(_kv2);

            // Sleep to let the cache expire
            Thread.Sleep(cacheExpirationTimeSpan);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Chrome", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("SuperUsers", config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
        }


        [Fact]
        public void SkipRefreshIfCacheNotExpired()
        {
            var featureFlags = new List<ConfigurationSetting> { _kv };

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            IConfigurationRefresher refresher = null;
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.UseFeatureFlags(o => o.CacheExpirationInterval = TimeSpan.FromSeconds(10));

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Firefox", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["FeatureManagement:Beta:EnabledFor:1:Name"]);
            Assert.Equal("20", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Percentage"]);
            Assert.Equal("US", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Region"]);
            Assert.Equal("SuperUsers", config["FeatureManagement:Beta:EnabledFor:2:Name"]);
            Assert.Equal("TimeWindow", config["FeatureManagement:Beta:EnabledFor:3:Name"]);
            Assert.Equal("/Date(1578643200000)/", config["FeatureManagement:Beta:EnabledFor:3:Parameters:Start"]);
            Assert.Equal("/Date(1578686400000)/", config["FeatureManagement:Beta:EnabledFor:3:Parameters:End"]);

            featureFlags[0] = ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "myFeature",
                value: @"
                        {
                          ""id"": ""Beta"",
                          ""description"": ""The new beta version of our web site."",
                          ""display_name"": ""Beta Feature"",
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
                        }
                        ",
                label: default,
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            featureFlags.Add(_kv2);

            refresher.RefreshAsync().Wait();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Firefox", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Null(config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
        }

        [Fact]
        public void WatchesFeatureFlagsUsingRefreshKeys()
        {
            var featureFlags = new List<ConfigurationSetting> { _kv };

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var refreshKeyConfigurationSetting = new ConfigurationSetting("MyRefreshKey", "1");

            mockClient.Setup(c => c.GetConfigurationSettingAsync("MyRefreshKey", LabelFilter.Null, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<Response<ConfigurationSetting>>(new MockResponse<ConfigurationSetting>(refreshKeyConfigurationSetting)));

            IConfigurationRefresher refresher = null;
            var cacheExpirationTimeSpan = TimeSpan.FromSeconds(1);
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object; ;
                    options.UseFeatureFlags(o =>
                    {
                        o.CacheExpirationInterval = cacheExpirationTimeSpan;
                        o.RegisterRefreshKey("MyRefreshKey");
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Firefox", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["FeatureManagement:Beta:EnabledFor:1:Name"]);
            Assert.Equal("20", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Percentage"]);
            Assert.Equal("US", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Region"]);
            Assert.Equal("SuperUsers", config["FeatureManagement:Beta:EnabledFor:2:Name"]);
            Assert.Equal("TimeWindow", config["FeatureManagement:Beta:EnabledFor:3:Name"]);
            Assert.Equal("/Date(1578643200000)/", config["FeatureManagement:Beta:EnabledFor:3:Parameters:Start"]);
            Assert.Equal("/Date(1578686400000)/", config["FeatureManagement:Beta:EnabledFor:3:Parameters:End"]);

            featureFlags[0] = ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "myFeature",
                value: @"
                        {
                          ""id"": ""Beta"",
                          ""description"": ""The new beta version of our web site."",
                          ""display_name"": ""Beta Feature"",
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
                        }
                        ",
                label: default,
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            featureFlags.Add(_kv2);

            // Wait for the cache to expire
            Thread.Sleep(cacheExpirationTimeSpan);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(refreshKeyConfigurationSetting, true, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<Response<ConfigurationSetting>>(new MockResponse<ConfigurationSetting>(refreshKeyConfigurationSetting, new MockResponse((int)HttpStatusCode.NoContent))));
            refresher.RefreshAsync().Wait();

            // Values should not have changed as the returned status code is not 200 OK
            Assert.Equal("Firefox", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);

            // Wait for the cache to expire
            Thread.Sleep(cacheExpirationTimeSpan);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(refreshKeyConfigurationSetting, true, It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult<Response<ConfigurationSetting>>(new MockResponse<ConfigurationSetting>(refreshKeyConfigurationSetting, new MockResponse((int)HttpStatusCode.OK))));
            refresher.RefreshAsync().Wait();

            // Values should have changed as the returned status code is 200 OK
            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Chrome", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("SuperUsers", config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
        }

        [Fact]
        public void PreservesDefaultQuery()
        {
            var response = new MockResponse(200);
            response.SetContent(SerializationHelpers.Serialize(new[] { _kv }, TestHelpers.SerializeBatch));

            var mockTransport = new MockTransport(response);
            var clientOptions = new ConfigurationClientOptions
            {
                Transport = mockTransport
            };

            var builder = new ConfigurationBuilder();
            builder.AddAzureAppConfiguration(options =>
            {
                options.Client = new ConfigurationClient(TestHelpers.CreateMockEndpointString(), clientOptions);
                options.UseFeatureFlags();
            }).Build();

            MockRequest request = mockTransport.SingleRequest;

            Assert.Contains("/kv/?key=%252A&label=%2500", Uri.EscapeUriString(request.Uri.PathAndQuery));
            Assert.DoesNotContain(Uri.EscapeDataString(FeatureManagementConstants.FeatureFlagMarker), request.Uri.PathAndQuery);
        }

        [Fact]
        public void QueriesFeatureFlags()
        {
            var mockTransport = new MockTransport(req =>
            {
                var response = new MockResponse(200);
                response.SetContent(SerializationHelpers.Serialize(new[] { _kv }, TestHelpers.SerializeBatch));
                return response;
            });

            var clientOptions = new ConfigurationClientOptions
            {
                Transport = mockTransport
            };

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = new ConfigurationClient(TestHelpers.CreateMockEndpointString(), clientOptions);
                    options.UseFeatureFlags(o => o.Label = "myLabel");
                })
                .Build();

            bool performedDefaultQuery = mockTransport.Requests.Any(r => r.Uri.PathAndQuery.Contains("/kv/?key=%2A&label=%00"));
            bool queriedFeatureFlags = mockTransport.Requests.Any(r => r.Uri.PathAndQuery.Contains(Uri.EscapeDataString(FeatureManagementConstants.FeatureFlagMarker)));

            Assert.True(performedDefaultQuery);
            Assert.True(queriedFeatureFlags);
        }

        [Fact]
        public void UsesEtagForFeatureFlagRefresh()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            IConfigurationRefresher refresher = null;
            var cacheExpirationTimeSpan = TimeSpan.FromSeconds(1);
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.UseFeatureFlags(o => o.CacheExpirationInterval = cacheExpirationTimeSpan);

                    refresher = options.GetRefresher();
                })
                .Build();

            // Sleep to let the cache expire
            Thread.Sleep(cacheExpirationTimeSpan);

            refresher.TryRefreshAsync().Wait();
            mockClient.Verify(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}
