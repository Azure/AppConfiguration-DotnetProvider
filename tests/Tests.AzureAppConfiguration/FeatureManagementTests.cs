// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Diagnostics;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Data.AppConfiguration.Tests;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
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
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
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
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
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
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
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
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
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
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),
        };

        ConfigurationSetting FirstKeyValue = ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey1",
                label: "label",
                value: "TestValue1",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType: "text");

        private ConfigurationSetting _variantsKv = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "VariantsFeature",
            value: @"
                    {
                        ""id"": ""VariantsFeature"",
                        ""description"": """",
                        ""display_name"": ""Variants Feature"",
                        ""enabled"": true,
                        ""conditions"": {
                        ""client_filters"": [
                            {
                            ""name"": ""AlwaysOn""
                            }
                        ]
                        },
                        ""variants"": [
		                {
			                ""name"": ""Big"",
			                ""configuration_value"": ""600px""
		                },
		                {
			                ""name"": ""Small"",
			                ""configuration_reference"": ""ShoppingCart:Small"",
			                ""status_override"": ""Disabled""
		                }
	                    ],
	                    ""allocation"": {
		                    ""seed"": ""13992821"",
		                    ""default_when_disabled"": ""Small"",
		                    ""default_when_enabled"": ""Small"",
		                    ""user"": [
			                    {
				                    ""variant"": ""Big"",
				                    ""users"": [
					                    ""Marsha"",
                                        ""John""
				                    ]
			                    },
                                {
                                    ""variant"": ""Small"",
                                    ""users"": [
                                        ""Alice"",
                                        ""Bob""
                                    ]
                                }   
		                    ],
		                    ""group"": [
			                    {
				                    ""variant"": ""Big"",
				                    ""groups"": [
					                    ""Ring1""
				                    ]
			                    },
                                {
                                    ""variant"": ""Small"",
                                    ""groups"": [
                                        ""Ring2"",
                                        ""Ring3""
                                    ]
                                }
		                    ],
		                    ""percentile"": [
			                    {
				                    ""variant"": ""Big"",
				                    ""from"": 0,
				                    ""to"": 50
			                    },
                                {
                                    ""variant"": ""Small"",
                                    ""from"": 50,
                                    ""to"": 100
                                }
		                    ]
	                    }
                    }
                    ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"));

        private ConfigurationSetting _telemetryKv = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "TelemetryFeature",
            value: @"
                    {
                        ""id"": ""TelemetryFeature"",
                        ""description"": """",
                        ""display_name"": ""Telemetry Feature"",
                        ""enabled"": true,
                        ""conditions"": {
                        ""client_filters"": [
                            {
                            ""name"": ""AlwaysOn""
                            }
                        ]
                        },
                        ""telemetry"": {
                            ""enabled"": true,
                            ""metadata"": {
		                        ""Tags.Tag1"": ""Tag1Value"",
		                        ""Tags.Tag2"": ""Tag2Value""
	                        }
                        }
                    }
                    ",
            label: "label",
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"));

        TimeSpan CacheExpirationTime = TimeSpan.FromSeconds(1);

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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            IConfigurationRefresher refresher = null;
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
        public void PreservesDefaultQuery()
        {
            var mockTransport = new MockTransport(req =>
            {
                var response = new MockResponse(200);
                response.SetContent(SerializationHelpers.Serialize(new[] { _kv }, TestHelpers.SerializeBatch));
                return response;
            });

            var options = new AzureAppConfigurationOptions();
            options.ClientOptions.Transport = mockTransport;
            var clientManager = TestHelpers.CreateMockedConfigurationClientManager(options);

            var builder = new ConfigurationBuilder();
            builder.AddAzureAppConfiguration(options =>
            {
                options.ClientManager = clientManager;
                options.UseFeatureFlags();
            }).Build();

            bool performedDefaultQuery = mockTransport.Requests.Any(r => r.Uri.PathAndQuery.Contains("/kv?key=%2A&label=%00"));
            bool queriedFeatureFlags = mockTransport.Requests.Any(r => r.Uri.PathAndQuery.Contains(Uri.EscapeDataString(FeatureManagementConstants.FeatureFlagMarker)));

            Assert.True(performedDefaultQuery);
            Assert.True(queriedFeatureFlags);
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
            var clientManager = TestHelpers.CreateMockedConfigurationClientManager(options);
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = clientManager;
                    options.UseFeatureFlags(o => o.Label = "myLabel");
                })
                .Build();

            bool performedDefaultQuery = mockTransport.Requests.Any(r => r.Uri.PathAndQuery.Contains("/kv?key=%2A&label=%00"));
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.UseFeatureFlags(o => o.CacheExpirationInterval = cacheExpirationTimeSpan);

                    refresher = options.GetRefresher();
                })
                .Build();

            // Sleep to let the cache expire
            Thread.Sleep(cacheExpirationTimeSpan);

            refresher.TryRefreshAsync().Wait();
            mockClient.Verify(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
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
        public void KeepSelectorPrecedenceAfterDedup()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var prefix = "Feature1";
            var label1 = "App1_Label";
            var label2 = "App2_Label";

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    return new MockAsyncPageable(_featureFlagCollection.Where(s =>
                        (s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + prefix) && s.Label == label1) ||
                        (s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + prefix) && s.Label == label2)).ToList());
                });

            var testClient = mockClient.Object;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(prefix + "*", label1); // to be deduped
                        ff.Select(prefix + "*", label2); // lower precedence
                        ff.Select(prefix + "*", label1); // higher precedence, taking effect
                    });
                })
                .Build();
            // label: App1_Label has higher precedence
            Assert.Equal("True", config["FeatureManagement:Feature1"]);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
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
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            // Sleep to let the cache for feature flag with label1 expire
            Thread.Sleep(cacheExpiration);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Browser", config["FeatureManagement:Feature1:EnabledFor:0:Name"]);
            Assert.Equal("Chrome", config["FeatureManagement:Feature1:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["FeatureManagement:Feature1:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
        }

        [Fact]
        public void ValidateCorrectFeatureFlagLoggedIfModifiedOrRemovedDuringRefresh()
        {
            IConfigurationRefresher refresher = null;
            var featureFlags = new List<ConfigurationSetting> { _kv2 };

            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            string informationalInvocation = "";
            string verboseInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Informational)
                    {
                        informationalInvocation += s;
                    }
                    if (args.Level == EventLevel.Verbose)
                    {
                        verboseInvocation += s;
                    }
                }, EventLevel.Verbose);

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.UseFeatureFlags(o => o.CacheExpirationInterval = CacheExpirationTime);
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("SuperUsers", config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);

            featureFlags[0] = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "myFeature1",
            value: @"
                    {
                      ""id"": ""MyFeature"",
                      ""description"": ""The new beta version of our web site."",
                      ""display_name"": ""Beta Feature"",
                      ""enabled"": true,
                      ""conditions"": {
                        ""client_filters"": [                        
                          {
                            ""name"": ""AllUsers""
                          }
                        ]
                      }
                    }
                    ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            Thread.Sleep(CacheExpirationTime);
            refresher.TryRefreshAsync().Wait();
            Assert.Equal("AllUsers", config["FeatureManagement:MyFeature:EnabledFor:0:Name"]);
            Assert.Contains(LogHelper.BuildFeatureFlagReadMessage("myFeature1", null, TestHelpers.PrimaryConfigStoreEndpoint.ToString().TrimEnd('/')), verboseInvocation);
            Assert.Contains(LogHelper.BuildFeatureFlagUpdatedMessage("myFeature1"), informationalInvocation);

            featureFlags.RemoveAt(0);
            Thread.Sleep(CacheExpirationTime);
            refresher.TryRefreshAsync().Wait();

            Assert.Null(config["FeatureManagement:MyFeature:EnabledFor:0:Name"]);
            Assert.Contains(LogHelper.BuildFeatureFlagReadMessage("myFeature1", null, TestHelpers.PrimaryConfigStoreEndpoint.ToString().TrimEnd('/')), verboseInvocation);
            Assert.Contains(LogHelper.BuildFeatureFlagUpdatedMessage("myFeature1"), informationalInvocation);
        }

        [Fact]
        public void ValidateFeatureFlagsUnchangedLogged()
        {
            IConfigurationRefresher refresher = null;
            var featureFlags = new List<ConfigurationSetting> { _kv2 };

            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            string verboseInvocation = "";
            using var _ = new AzureEventSourceListener(
                (args, s) =>
                {
                    if (args.Level == EventLevel.Verbose)
                    {
                        verboseInvocation += s;
                    }
                }, EventLevel.Verbose);

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.UseFeatureFlags(o => o.CacheExpirationInterval = CacheExpirationTime);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(CacheExpirationTime);
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("SuperUsers", config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
            FirstKeyValue.Value = "newValue1";

            Thread.Sleep(CacheExpirationTime);
            refresher.TryRefreshAsync().Wait();
            Assert.Equal("SuperUsers", config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
            Assert.Contains(LogHelper.BuildFeatureFlagsUnchangedMessage(TestHelpers.PrimaryConfigStoreEndpoint.ToString().TrimEnd('/')), verboseInvocation);
        }

        [Fact]
        public void MapTransformFeatureFlagWithRefresh()
        {
            ConfigurationSetting _kv = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "myFeature",
            value: @"
                                {
                                    ""id"": ""MyFeature"",
                                    ""description"": ""The new beta version of our web site."",
                                    ""display_name"": ""Beta Feature"",
                                    ""enabled"": true,
                                    ""conditions"": {
                                    ""client_filters"": [
                                        {
                                        ""name"": ""AllUsers""
                                        }, 
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

            IConfigurationRefresher refresher = null;
            var featureFlags = new List<ConfigurationSetting> { _kv };
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
            .Returns(new MockAsyncPageable(featureFlags));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            var mockClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = mockClientManager;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label", true)
                            .SetCacheExpiration(CacheExpirationTime);
                    });
                    options.UseFeatureFlags(o => o.CacheExpirationInterval = CacheExpirationTime);
                    options.Map((setting) =>
                    {
                        if (setting.ContentType == FeatureManagementConstants.ContentType + ";charset=utf-8")
                        {
                            setting.Value = @"
                                {
                                    ""id"": ""MyFeature"",
                                    ""description"": ""The new beta version of our web site."",
                                    ""display_name"": ""Beta Feature"",
                                    ""enabled"": true,
                                    ""conditions"": {
                                    ""client_filters"": [
                                        {
                                        ""name"": ""NoUsers""
                                        }, 
                                        {
                                        ""name"": ""SuperUsers""
                                        }
                                    ]
                                    }
                                }
                                ";
                        }
                        return new ValueTask<ConfigurationSetting>(setting);
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("NoUsers", config["FeatureManagement:MyFeature:EnabledFor:0:Name"]);

            FirstKeyValue.Value = "newValue1";
            featureFlags[0] = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "myFeature",
            value: @"
                                {
                                  ""id"": ""MyFeature"",
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
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            Thread.Sleep(CacheExpirationTime);
            refresher.TryRefreshAsync().Wait();

            Assert.Equal("newValue1", config["TestKey1"]);
            Assert.Equal("NoUsers", config["FeatureManagement:MyFeature:EnabledFor:0:Name"]);
        }

        [Fact]
        public void WithVariants()
        {
            var featureFlags = new List<ConfigurationSetting>()
            {
                _variantsKv
            };

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.UseFeatureFlags();
                })
                .Build();

            Assert.Equal("Big", config["FeatureManagement:VariantsFeature:Variants:0:Name"]);
            Assert.Equal("600px", config["FeatureManagement:VariantsFeature:Variants:0:ConfigurationValue"]);
            Assert.Equal("Small", config["FeatureManagement:VariantsFeature:Variants:1:Name"]);
            Assert.Equal("ShoppingCart:Small", config["FeatureManagement:VariantsFeature:Variants:1:ConfigurationReference"]);
            Assert.Equal("Disabled", config["FeatureManagement:VariantsFeature:Variants:1:StatusOverride"]);
            Assert.Equal("Small", config["FeatureManagement:VariantsFeature:Allocation:DefaultWhenDisabled"]);
            Assert.Equal("Small", config["FeatureManagement:VariantsFeature:Allocation:DefaultWhenEnabled"]);
            Assert.Equal("Big", config["FeatureManagement:VariantsFeature:Allocation:User:0:Variant"]);
            Assert.Equal("Marsha", config["FeatureManagement:VariantsFeature:Allocation:User:0:Users:0"]);
            Assert.Equal("John", config["FeatureManagement:VariantsFeature:Allocation:User:0:Users:1"]);
            Assert.Equal("Small", config["FeatureManagement:VariantsFeature:Allocation:User:1:Variant"]);
            Assert.Equal("Alice", config["FeatureManagement:VariantsFeature:Allocation:User:1:Users:0"]);
            Assert.Equal("Bob", config["FeatureManagement:VariantsFeature:Allocation:User:1:Users:1"]);
            Assert.Equal("Big", config["FeatureManagement:VariantsFeature:Allocation:Group:0:Variant"]);
            Assert.Equal("Ring1", config["FeatureManagement:VariantsFeature:Allocation:Group:0:Groups:0"]);
            Assert.Equal("Small", config["FeatureManagement:VariantsFeature:Allocation:Group:1:Variant"]);
            Assert.Equal("Ring2", config["FeatureManagement:VariantsFeature:Allocation:Group:1:Groups:0"]);
            Assert.Equal("Ring3", config["FeatureManagement:VariantsFeature:Allocation:Group:1:Groups:1"]);
            Assert.Equal("Big", config["FeatureManagement:VariantsFeature:Allocation:Percentile:0:Variant"]);
            Assert.Equal("0", config["FeatureManagement:VariantsFeature:Allocation:Percentile:0:From"]);
            Assert.Equal("50", config["FeatureManagement:VariantsFeature:Allocation:Percentile:0:To"]);
            Assert.Equal("Small", config["FeatureManagement:VariantsFeature:Allocation:Percentile:1:Variant"]);
            Assert.Equal("50", config["FeatureManagement:VariantsFeature:Allocation:Percentile:1:From"]);
            Assert.Equal("100", config["FeatureManagement:VariantsFeature:Allocation:Percentile:1:To"]);
            Assert.Equal("13992821", config["FeatureManagement:VariantsFeature:Allocation:Seed"]);
        }

        [Fact]
        public void WithTelemetry()
        {
            var featureFlags = new List<ConfigurationSetting>()
            {
                _telemetryKv
            };

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Connect(TestHelpers.PrimaryConfigStoreEndpoint, new DefaultAzureCredential());
                    options.UseFeatureFlags();
                })
                .Build();

            Assert.Equal("True", config["FeatureManagement:TelemetryFeature:Telemetry:Enabled"]);
            Assert.Equal("Tag1Value", config["FeatureManagement:TelemetryFeature:Telemetry:Metadata:Tags.Tag1"]);
            Assert.Equal("Tag2Value", config["FeatureManagement:TelemetryFeature:Telemetry:Metadata:Tags.Tag2"]);
            Assert.Equal("c3c231fd-39a0-4cb6-3237-4614474b92c1", config["FeatureManagement:TelemetryFeature:Telemetry:Metadata:ETag"]);

            byte[] featureFlagIdHash;

            using (HashAlgorithm hashAlgorithm = SHA256.Create())
            {
                featureFlagIdHash = hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes($"{FeatureManagementConstants.FeatureFlagMarker}TelemetryFeature\nlabel"));
            }

            string featureFlagId = WebUtility.UrlEncode(Convert.ToBase64String(featureFlagIdHash));

            Assert.Equal(featureFlagId, config["FeatureManagement:TelemetryFeature:Telemetry:Metadata:FeatureFlagId"]);
            Assert.Equal($"{TestHelpers.PrimaryConfigStoreEndpoint}kv/{FeatureManagementConstants.FeatureFlagMarker}TelemetryFeature?label=label", config["FeatureManagement:TelemetryFeature:Telemetry:Metadata:FeatureFlagReference"]);
        }


        [Fact]
        public void WithRequirementType()
        {
            var emptyFilters = "[]";
            var nonEmptyFilters = @"[
                {
                    ""name"": ""FilterA"",
                    ""parameters"": {
                        ""Foo"": ""Bar""
                    }
                },
                {
                    ""name"": ""FilterB""
                }
            ]";
            var featureFlags = new List<ConfigurationSetting>()
            {
                _kv2,
                FeatureWithRequirementType("Feature_NoFilters", "All", emptyFilters),
                FeatureWithRequirementType("Feature_RequireAll", "All", nonEmptyFilters),
                FeatureWithRequirementType("Feature_RequireAny", "Any", nonEmptyFilters)
            };

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.UseFeatureFlags();
                })
                .Build();

            Assert.Null(config["FeatureManagement:MyFeature2:RequirementType"]);
            Assert.Null(config["FeatureManagement:Feature_NoFilters:RequirementType"]);
            Assert.Equal("All", config["FeatureManagement:Feature_RequireAll:RequirementType"]);
            Assert.Equal("Any", config["FeatureManagement:Feature_RequireAny:RequirementType"]);
        }

        Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
        {
            return Response.FromValue(FirstKeyValue, new MockResponse(200));
        }

        Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
        {
            return Response.FromValue(TestHelpers.CloneSetting(FirstKeyValue), new Mock<Response>().Object);
        }

        private ConfigurationSetting FeatureWithRequirementType(string featureId, string requirementType, string clientFiltersJsonString)
        {
            return ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + featureId,
                value: $@"
                        {{
                          ""id"": ""{featureId}"",
                          ""enabled"": true,
                          ""conditions"": {{
                            ""requirement_type"": ""{requirementType}"",
                            ""client_filters"": {clientFiltersJsonString}
                          }}
                        }}
                        ",
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"));
        }
    }
}
