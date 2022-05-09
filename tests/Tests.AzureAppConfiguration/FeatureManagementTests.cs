// Copyright (c) Microsoft Corporation.
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
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    // This attribute ensures that feature management v1 and v2 tests are never run in parallel.
    // Since feature flag behavior is controlled by an environment variable, running them in parallel has side effects.
    [Collection("Feature Management Test Collection")]
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
            contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
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
            contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"));

        List<ConfigurationSetting> _featureFlagCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "App1_Feature1",
                value: @"
                        {
                          ""id"": ""App1_Feature1"",
                          ""enabled"": true,
                          ""conditions"": {
                            ""client_filters"": []
                          }
                        }
                        ",
                label: "App1_Label",
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "App1_Feature2",
                value: @"
                        {
                          ""id"": ""App1_Feature2"",
                          ""enabled"": false,
                          ""conditions"": {
                            ""client_filters"": []
                          }
                        }
                        ",
                label: "App1_Label",
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "Feature1",
                value: @"
                        {
                          ""id"": ""Feature1"",
                          ""enabled"": false,
                          ""conditions"": {
                            ""client_filters"": []
                          }
                        }
                        ",
                label: "App1_Label",
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "App2_Feature1",
                value: @"
                        {
                          ""id"": ""App2_Feature1"",
                          ""enabled"": false,
                          ""conditions"": {
                            ""client_filters"": []
                          }
                        }
                        ",
                label: "App2_Label",
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "App2_Feature2",
                value: @"
                        {
                          ""id"": ""App2_Feature2"",
                          ""enabled"": true,
                          ""conditions"": {
                            ""client_filters"": []
                          }
                        }
                        ",
                label: "App2_Label",
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "Feature1",
                value: @"
                        {
                          ""id"": ""Feature1"",
                          ""enabled"": true,
                          ""conditions"": {
                            ""client_filters"": []
                          }
                        }
                        ",
                label: "App2_Label",
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),
        };

        [Fact]
        public void UsesFeatureFlags()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            var featureFlags = new List<ConfigurationSetting> { _kv };

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var testClient = mockClient.Object;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(testClient);
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
        public void WatchesFeatureFlags()
        {
            var featureFlags = new List<ConfigurationSetting> { _kv };

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            IConfigurationRefresher refresher = null;
            var cacheExpirationTimeSpan = TimeSpan.FromSeconds(1);
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
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
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
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
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            IConfigurationRefresher refresher = null;
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
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
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            featureFlags.Add(_kv2);

            refresher.RefreshAsync().Wait();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Firefox", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Null(config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
        }

        [Fact]
        public void PreservesDefaultQuery()
        {
            var response = new MockResponse(200);
            response.SetContent(SerializationHelpers.Serialize(new[] { _kv }, TestHelpers.SerializeBatch));

            var mockTransport = new MockTransport(response);
            var options = new AzureAppConfigurationOptions();
            options.ClientOptions.Transport = mockTransport;

            var builder = new ConfigurationBuilder();
            var clientProvider = TestHelpers.CreateMockedConfigurationClientProvider(options);
            builder.AddAzureAppConfiguration(options =>
            {
                options.ClientProvider = clientProvider;
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

            var options = new AzureAppConfigurationOptions();
            options.ClientOptions.Transport = mockTransport;
            var clientProvider = TestHelpers.CreateMockedConfigurationClientProvider(options);
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = clientProvider;
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.UseFeatureFlags(o => o.CacheExpirationInterval = cacheExpirationTimeSpan);

                    refresher = options.GetRefresher();
                })
                .Build();

            // Sleep to let the cache expire
            Thread.Sleep(cacheExpirationTimeSpan);

            refresher.TryRefreshAsync().Wait();
            mockClient.Verify(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public void SelectFeatureFlags()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var featureFlagPrefix = "App1";
            var labelFilter = "App1_Label";
            var cacheExpiration = TimeSpan.FromSeconds(1);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_featureFlagCollection.Where(s => s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + featureFlagPrefix) && s.Label == labelFilter).ToList()));

            var testClient = mockClient.Object;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(testClient);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.CacheExpirationInterval = cacheExpiration;
                        ff.Select(featureFlagPrefix + "*", labelFilter);
                    });
                })
                .Build();

            Assert.Equal("True", config["FeatureManagement:App1_Feature1"]);
            Assert.Equal("False", config["FeatureManagement:App1_Feature2"]);

            // Verify that the feature flag that did not start with the specified prefix was not loaded
            Assert.Null(config["FeatureManagement:Feature1"]);

            // Verify that the feature flag that did not match the specified label was not loaded
            Assert.Null(config["FeatureManagement:App2_Feature1"]);
            Assert.Null(config["FeatureManagement:App2_Feature2"]);
        }

        [Fact]
        public void MultipleSelectsInSameUseFeatureFlags()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var prefix1 = "App1";
            var prefix2 = "App2";
            var label1 = "App1_Label";
            var label2 = "App2_Label";

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    return new MockAsyncPageable(_featureFlagCollection.Where(s =>
                        (s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + prefix1) && s.Label == label1) ||
                        (s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + prefix2) && s.Label == label2)).ToList());
                });

            var testClient = mockClient.Object;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(testClient);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(prefix1 + "*", label1);
                        ff.Select(prefix2 + "*", label2);
                    });
                })
                .Build();

            Assert.Equal("True", config["FeatureManagement:App1_Feature1"]);
            Assert.Equal("False", config["FeatureManagement:App1_Feature2"]);
            Assert.Equal("False", config["FeatureManagement:App2_Feature1"]);
            Assert.Equal("True", config["FeatureManagement:App2_Feature2"]);

            // Verify that the feature flag that did not start with the specified prefix was not loaded
            Assert.Null(config["FeatureManagement:Feature1"]);
        }

        [Fact]
        public void UseFeatureFlagsThrowsIfBothSelectAndLabelPresent()
        {
            void action() => new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select("MyApp*", "Label1");
                        ff.Label = "Label1";
                    });
                })
                .Build();

            Assert.Throws<ArgumentException>(action);
        }

        [Fact]
        public void UseFeatureFlagsThrowsIfFeatureFlagFilterIsInvalid()
        {
            void action() => new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(@"MyApp\*", "Label1");
                        ff.Label = "Label1";
                    });
                })
                .Build();

            Assert.Throws<ArgumentException>(action);
        }

        [Fact]
        public void MultipleCallsToUseFeatureFlags()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var prefix1 = "App1";
            var prefix2 = "App2";
            var label1 = "App1_Label";
            var label2 = "App2_Label";

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    return new MockAsyncPageable(_featureFlagCollection.Where(s =>
                        (s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + prefix1) && s.Label == label1) || 
                        (s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + prefix2) && s.Label == label2)).ToList());
                });

            var testClient = mockClient.Object;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(testClient);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(prefix1 + "*", label1);
                    });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(prefix2 + "*", label2);
                    });
                })
                .Build();

            Assert.Equal("True", config["FeatureManagement:App1_Feature1"]);
            Assert.Equal("False", config["FeatureManagement:App1_Feature2"]);
            Assert.Equal("False", config["FeatureManagement:App2_Feature1"]);
            Assert.Equal("True", config["FeatureManagement:App2_Feature2"]);

            // Verify that the feature flag that did not start with the specified prefix was not loaded
            Assert.Null(config["FeatureManagement:Feature1"]);
        }

        [Fact]
        public void MultipleCallsToUseFeatureFlagsWithSelectAndLabel()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var prefix1 = "App1";
            var label1 = "App1_Label";
            var label2 = "App2_Label";

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    return new MockAsyncPageable(_featureFlagCollection.Where(s =>
                        (s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + prefix1) && s.Label == label1) ||
                        (s.Label == label2)).ToList());
                });

            var testClient = mockClient.Object;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(testClient);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(prefix1 + "*", label1);
                    });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Label = label2;
                    });
                })
                .Build();

            // Loaded from prefix1 and label1
            Assert.Equal("True", config["FeatureManagement:App1_Feature1"]);
            Assert.Equal("False", config["FeatureManagement:App1_Feature2"]);

            // Loaded from label2
            Assert.Equal("False", config["FeatureManagement:App2_Feature1"]);
            Assert.Equal("True", config["FeatureManagement:App2_Feature2"]);
            Assert.Equal("True", config["FeatureManagement:Feature1"]);
        }

        [Fact]
        public void DifferentCacheExpirationsForMultipleFeatureFlagRegistrations()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var prefix1 = "App1";
            var prefix2 = "App2";
            var label1 = "App1_Label";
            var label2 = "App2_Label";
            var cacheExpiration1 = TimeSpan.FromSeconds(1);
            var cacheExpiration2 = TimeSpan.FromSeconds(60);
            IConfigurationRefresher refresher = null;
            var featureFlagCollection = new List<ConfigurationSetting>(_featureFlagCollection);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    return new MockAsyncPageable(featureFlagCollection.Where(s =>
                        (s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + prefix1) && s.Label == label1) ||
                        (s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + prefix2) && s.Label == label2 && s.Key != FeatureManagementConstants.FeatureFlagMarker + "App2_Feature3")).ToList());
                });

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.CacheExpirationInterval = cacheExpiration1;
                        ff.Select(prefix1 + "*", label1);
                    });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.CacheExpirationInterval = cacheExpiration2;
                        ff.Select(prefix2 + "*", label2);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("True", config["FeatureManagement:App1_Feature1"]);
            Assert.Equal("False", config["FeatureManagement:App1_Feature2"]);
            Assert.Equal("False", config["FeatureManagement:App2_Feature1"]);
            Assert.Equal("True", config["FeatureManagement:App2_Feature2"]);

            // update the value of App1_Feature1 feature flag with label1
            featureFlagCollection[0] = ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "App1_Feature1",
                value: @"
                        {
                          ""id"": ""App1_Feature1"",
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
                label: "App1_Label",
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            // add new feature flag with label2
            featureFlagCollection.Add(ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "App2_Feature3",
                value: @"
                        {
                          ""id"": ""App2_Feature3"",
                          ""enabled"": true,
                          ""conditions"": {
                            ""client_filters"": []
                          }
                        }
                        ",
                label: "App2_Label",
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f")));

            // Sleep to let the cache for feature flag with label1 expire
            Thread.Sleep(cacheExpiration1);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Browser", config["FeatureManagement:App1_Feature1:EnabledFor:0:Name"]);
            Assert.Equal("Chrome", config["FeatureManagement:App1_Feature1:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["FeatureManagement:App1_Feature1:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("False", config["FeatureManagement:App1_Feature2"]);
            Assert.Equal("False", config["FeatureManagement:App2_Feature1"]);
            Assert.Equal("True", config["FeatureManagement:App2_Feature2"]);

            // even though App2_Feature3 feature flag has been added, its value should not be loaded in config because label2 cache has not expired
            Assert.Null(config["FeatureManagement:App2_Feature3"]);
        }

        [Fact]
        public void OverwrittenCacheExpirationForSameFeatureFlagRegistrations()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var cacheExpiration1 = TimeSpan.FromSeconds(1);
            var cacheExpiration2 = TimeSpan.FromSeconds(60);
            IConfigurationRefresher refresher = null;
            var featureFlagCollection = new List<ConfigurationSetting>(_featureFlagCollection);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlagCollection));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select("*", "App1_Label");
                        ff.Select("*", "App2_Label");
                        ff.CacheExpirationInterval = cacheExpiration1;
                    });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select("*", "App1_Label");
                        ff.Select("*", "App2_Label");
                        ff.CacheExpirationInterval = cacheExpiration2;
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("True", config["FeatureManagement:App1_Feature1"]);
            Assert.Equal("False", config["FeatureManagement:App1_Feature2"]);
            Assert.Equal("False", config["FeatureManagement:App2_Feature1"]);
            Assert.Equal("True", config["FeatureManagement:App2_Feature2"]);
            Assert.Equal("True", config["FeatureManagement:Feature1"]);

            // update the value of App1_Feature1 feature flag with label1
            featureFlagCollection[0] = ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "App1_Feature1",
                value: @"
                        {
                          ""id"": ""App1_Feature1"",
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
                label: "App1_Label",
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            Thread.Sleep(cacheExpiration1);
            refresher.RefreshAsync().Wait();

            // The cache expiration time for feature flags was overwritten by second call to UseFeatureFlags.
            // Sleeping for cacheExpiration1 time should not update feature flags.
            Assert.Equal("True", config["FeatureManagement:App1_Feature1"]);
            Assert.Equal("False", config["FeatureManagement:App1_Feature2"]);
            Assert.Equal("False", config["FeatureManagement:App2_Feature1"]);
            Assert.Equal("True", config["FeatureManagement:App2_Feature2"]);
            Assert.Equal("True", config["FeatureManagement:Feature1"]);
        }

        [Fact]
        public void SelectAndRefreshSingleFeatureFlag()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var prefix1 = "Feature1";
            var label1 = "App1_Label";
            var cacheExpiration = TimeSpan.FromSeconds(1);
            IConfigurationRefresher refresher = null;
            var featureFlagCollection = new List<ConfigurationSetting>(_featureFlagCollection);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    return new MockAsyncPageable(featureFlagCollection.Where(s =>
                        s.Key.Equals(FeatureManagementConstants.FeatureFlagMarker + prefix1) && s.Label == label1).ToList());
                });

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.CacheExpirationInterval = cacheExpiration;
                        ff.Select(prefix1, label1);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("False", config["FeatureManagement:Feature1"]);

            // update the value of Feature1 feature flag with App1_Label
            featureFlagCollection[2] = ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "Feature1",
                value: @"
                        {
                          ""id"": ""Feature1"",
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
                label: "App1_Label",
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            // Sleep to let the cache for feature flag with label1 expire
            Thread.Sleep(cacheExpiration);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Browser", config["FeatureManagement:Feature1:EnabledFor:0:Name"]);
            Assert.Equal("Chrome", config["FeatureManagement:Feature1:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["FeatureManagement:Feature1:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
        }
    }
}
