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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        List<ConfigurationSetting> _variantFeatureFlagCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "VariantsFeature1",
                value: @"
                        {
                            ""id"": ""VariantsFeature1"",
                            ""enabled"": true,
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
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "VariantsFeature2",
                value: @"
                            {
                                ""id"": ""VariantsFeature2"",
                                ""enabled"": false,
                                ""variants"": [
		                        {
			                        ""name"": ""ObjectVariant"",
			                        ""configuration_value"": {
                                        ""Key1"": ""Value1"",
                                        ""Key2"": {
                                            ""InsideKey2"": ""Value2""
                                        }
                                    }
		                        },
		                        {
			                        ""name"": ""NumberVariant"",
			                        ""configuration_value"": 100
		                        },
		                        {
			                        ""name"": ""NullVariant"",
			                        ""configuration_value"": null
		                        },
		                        {
			                        ""name"": ""MissingValueVariant""
		                        },
		                        {
			                        ""name"": ""BooleanVariant"",
			                        ""configuration_value"": true
		                        }
	                            ],
	                            ""allocation"": {
		                            ""default_when_disabled"": ""ObjectVariant"",
		                            ""default_when_enabled"": ""ObjectVariant""
	                            }
                            }
                            ",
                label: default,
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "VariantsFeature3",
                value: @"
                            {
                                ""id"": ""VariantsFeature3"",
                                ""enabled"": ""true"",
                                ""variants"": [
		                        {
			                        ""name"": ""NumberVariant"",
			                        ""configuration_value"": 1
		                        },
		                        {
			                        ""name"": ""NumberVariant"",
			                        ""configuration_value"": 2
		                        },
		                        {
			                        ""name"": ""OtherVariant"",
			                        ""configuration_value"": ""Other""
		                        }
	                            ],
                                ""allocation"": {
                                    ""default_when_enabled"": ""OtherVariant"",
                                    ""default_when_enabled"": ""NumberVariant""
                                }
                            }
                            ",
                label: default,
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "VariantsFeature4",
                value: @"
                            {
                                ""id"": ""VariantsFeature4"",
                                ""enabled"": true,
                                ""variants"": null,
	                            ""allocation"": null
                            }
                            ",
                label: default,
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"))
        };

        List<ConfigurationSetting> _telemetryFeatureFlagCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "TelemetryFeature1",
                value: @"
                        {
                            ""id"": ""TelemetryFeature1"",
                            ""enabled"": true,
                            ""telemetry"": {
                                ""enabled"": ""true"",
                                ""metadata"": {
		                            ""Tags.Tag1"": ""Tag1Value"",
		                            ""Tags.Tag2"": ""Tag2Value""
	                            }
                            }
                        }
                        ",
                label: "label",
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "TelemetryFeature2",
                value: @"
                        {
                            ""id"": ""TelemetryFeature2"",
                            ""enabled"": true,
                            ""telemetry"": {
                                ""enabled"": false,
                                ""enabled"": true,
                                ""metadata"": {
		                            ""Tags.Tag1"": ""Tag1Value"",
		                            ""Tags.Tag1"": ""Tag2Value""
	                            }
                            }
                        }
                        ",
                label: "label",
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"))
        };

        TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

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

            Assert.Equal("Beta", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Firefox", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["feature_management:feature_flags:0:conditions:client_filters:1:name"]);
            Assert.Equal("20", config["feature_management:feature_flags:0:conditions:client_filters:1:parameters:Percentage"]);
            Assert.Equal("US", config["feature_management:feature_flags:0:conditions:client_filters:1:parameters:Region"]);
            Assert.Equal("SuperUsers", config["feature_management:feature_flags:0:conditions:client_filters:2:name"]);
            Assert.Equal("TimeWindow", config["feature_management:feature_flags:0:conditions:client_filters:3:name"]);
            Assert.Equal("/Date(1578643200000)/", config["feature_management:feature_flags:0:conditions:client_filters:3:parameters:Start"]);
            Assert.Equal("/Date(1578686400000)/", config["feature_management:feature_flags:0:conditions:client_filters:3:parameters:End"]);
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
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.UseFeatureFlags(o => o.SetRefreshInterval(RefreshInterval));

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Beta", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Firefox", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["feature_management:feature_flags:0:conditions:client_filters:1:name"]);
            Assert.Equal("20", config["feature_management:feature_flags:0:conditions:client_filters:1:parameters:Percentage"]);
            Assert.Equal("US", config["feature_management:feature_flags:0:conditions:client_filters:1:parameters:Region"]);
            Assert.Equal("SuperUsers", config["feature_management:feature_flags:0:conditions:client_filters:2:name"]);
            Assert.Equal("TimeWindow", config["feature_management:feature_flags:0:conditions:client_filters:3:name"]);
            Assert.Equal("/Date(1578643200000)/", config["feature_management:feature_flags:0:conditions:client_filters:3:parameters:Start"]);
            Assert.Equal("/Date(1578686400000)/", config["feature_management:feature_flags:0:conditions:client_filters:3:parameters:End"]);

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

            // Sleep to let the refresh interval elapse
            Thread.Sleep(RefreshInterval);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Beta", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Chrome", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
            Assert.Equal("SuperUsers", config["feature_management:feature_flags:1:conditions:client_filters:0:name"]);
        }

        [Fact]
        public void WatchesFeatureFlagsUsingCacheExpirationInterval()
        {
            var featureFlags = new List<ConfigurationSetting> { _kv };

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var cacheExpirationInterval = TimeSpan.FromSeconds(1);

            IConfigurationRefresher refresher = null;
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.UseFeatureFlags(o => o.CacheExpirationInterval = cacheExpirationInterval);

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Beta", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Firefox", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["feature_management:feature_flags:0:conditions:client_filters:1:name"]);
            Assert.Equal("20", config["feature_management:feature_flags:0:conditions:client_filters:1:parameters:Percentage"]);
            Assert.Equal("US", config["feature_management:feature_flags:0:conditions:client_filters:1:parameters:Region"]);
            Assert.Equal("SuperUsers", config["feature_management:feature_flags:0:conditions:client_filters:2:name"]);
            Assert.Equal("TimeWindow", config["feature_management:feature_flags:0:conditions:client_filters:3:name"]);
            Assert.Equal("/Date(1578643200000)/", config["feature_management:feature_flags:0:conditions:client_filters:3:parameters:Start"]);
            Assert.Equal("/Date(1578686400000)/", config["feature_management:feature_flags:0:conditions:client_filters:3:parameters:End"]);

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
            Thread.Sleep(cacheExpirationInterval);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Beta", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Chrome", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
            Assert.Equal("SuperUsers", config["feature_management:feature_flags:1:conditions:client_filters:0:name"]);
        }

        [Fact]
        public void SkipRefreshIfRefreshIntervalHasNotElapsed()
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
                    options.UseFeatureFlags(o => o.SetRefreshInterval(TimeSpan.FromSeconds(10)));

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Beta", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Firefox", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["feature_management:feature_flags:0:conditions:client_filters:1:name"]);
            Assert.Equal("20", config["feature_management:feature_flags:0:conditions:client_filters:1:parameters:Percentage"]);
            Assert.Equal("US", config["feature_management:feature_flags:0:conditions:client_filters:1:parameters:Region"]);
            Assert.Equal("SuperUsers", config["feature_management:feature_flags:0:conditions:client_filters:2:name"]);
            Assert.Equal("TimeWindow", config["feature_management:feature_flags:0:conditions:client_filters:3:name"]);
            Assert.Equal("/Date(1578643200000)/", config["feature_management:feature_flags:0:conditions:client_filters:3:parameters:Start"]);
            Assert.Equal("/Date(1578686400000)/", config["feature_management:feature_flags:0:conditions:client_filters:3:parameters:End"]);

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

            Assert.Equal("Beta", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Firefox", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
            Assert.Null(config["feature_management:feature_flags:1:conditions:client_filters:0:name"]);
        }

        [Fact]
        public void SkipRefreshIfCacheExpirationIntervalHasNotElapsed()
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

            Assert.Equal("Beta", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Firefox", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["feature_management:feature_flags:0:conditions:client_filters:1:name"]);
            Assert.Equal("20", config["feature_management:feature_flags:0:conditions:client_filters:1:parameters:Percentage"]);
            Assert.Equal("US", config["feature_management:feature_flags:0:conditions:client_filters:1:parameters:Region"]);
            Assert.Equal("SuperUsers", config["feature_management:feature_flags:0:conditions:client_filters:2:name"]);
            Assert.Equal("TimeWindow", config["feature_management:feature_flags:0:conditions:client_filters:3:name"]);
            Assert.Equal("/Date(1578643200000)/", config["feature_management:feature_flags:0:conditions:client_filters:3:parameters:Start"]);
            Assert.Equal("/Date(1578686400000)/", config["feature_management:feature_flags:0:conditions:client_filters:3:parameters:End"]);

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

            Assert.Equal("Beta", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Firefox", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
            Assert.Null(config["feature_management:feature_flags:1:conditions:client_filters:0:name"]);
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
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.UseFeatureFlags(o => o.SetRefreshInterval(RefreshInterval));

                    refresher = options.GetRefresher();
                })
                .Build();

            // Sleep to wait for refresh interval to elapse
            Thread.Sleep(RefreshInterval);

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

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_featureFlagCollection.Where(s => s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + featureFlagPrefix) && s.Label == labelFilter).ToList()));

            var testClient = mockClient.Object;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.SetRefreshInterval(RefreshInterval);
                        ff.Select(featureFlagPrefix + "*", labelFilter);
                    });
                })
                .Build();

            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("False", config["feature_management:feature_flags:1:enabled"]);

            // Verify that the feature flag that did not start with the specified prefix was not loaded
            Assert.Null(config["feature_management:feature_flags:2"]);

            // Verify that the feature flag that did not match the specified label was not loaded
            Assert.Null(config["feature_management:feature_flags:3"]);
            Assert.Null(config["feature_management:feature_flags:4"]);
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

            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("App1_Feature1", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:1:enabled"]);
            Assert.Equal("App1_Feature2", config["feature_management:feature_flags:1:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:2:enabled"]);
            Assert.Equal("App2_Feature1", config["feature_management:feature_flags:2:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:3:enabled"]);
            Assert.Equal("App2_Feature2", config["feature_management:feature_flags:3:id"]);

            // Verify that the feature flag that did not start with the specified prefix was not loaded
            Assert.Null(config["feature_management:feature_flags:4"]);
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
            Assert.Equal("Feature1", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
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

            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("App1_Feature1", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:1:enabled"]);
            Assert.Equal("App1_Feature2", config["feature_management:feature_flags:1:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:2:enabled"]);
            Assert.Equal("App2_Feature1", config["feature_management:feature_flags:2:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:3:enabled"]);
            Assert.Equal("App2_Feature2", config["feature_management:feature_flags:3:id"]);

            // Verify that the feature flag Feature1 did not start with the specified prefix was not loaded
            Assert.Null(config["feature_management:feature_flags:4"]);
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
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("App1_Feature1", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:1:enabled"]);
            Assert.Equal("App1_Feature2", config["feature_management:feature_flags:1:id"]);

            // Loaded from label2
            Assert.Equal("False", config["feature_management:feature_flags:2:enabled"]);
            Assert.Equal("App2_Feature1", config["feature_management:feature_flags:2:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:3:enabled"]);
            Assert.Equal("App2_Feature2", config["feature_management:feature_flags:3:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:4:enabled"]);
            Assert.Equal("Feature1", config["feature_management:feature_flags:4:id"]);
        }

        [Fact]
        public void DifferentRefreshIntervalsForMultipleFeatureFlagRegistrations()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var prefix1 = "App1";
            var prefix2 = "App2";
            var label1 = "App1_Label";
            var label2 = "App2_Label";
            var refreshInterval1 = TimeSpan.FromSeconds(1);
            var refreshInterval2 = TimeSpan.FromSeconds(60);
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
                        ff.SetRefreshInterval(refreshInterval1);
                        ff.Select(prefix1 + "*", label1);
                    });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.SetRefreshInterval(refreshInterval2);
                        ff.Select(prefix2 + "*", label2);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("App1_Feature1", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:1:enabled"]);
            Assert.Equal("App1_Feature2", config["feature_management:feature_flags:1:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:2:enabled"]);
            Assert.Equal("App2_Feature1", config["feature_management:feature_flags:2:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:3:enabled"]);
            Assert.Equal("App2_Feature2", config["feature_management:feature_flags:3:id"]);

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

            // Sleep to let the refresh interval for feature flag with label1 elapse
            Thread.Sleep(refreshInterval1);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Chrome", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
            Assert.Equal("False", config["feature_management:feature_flags:1:enabled"]);
            Assert.Equal("App1_Feature2", config["feature_management:feature_flags:1:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:2:enabled"]);
            Assert.Equal("App2_Feature1", config["feature_management:feature_flags:2:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:3:enabled"]);
            Assert.Equal("App2_Feature2", config["feature_management:feature_flags:3:id"]);

            // even though App2_Feature3 feature flag has been added, its value should not be loaded in config because label2 refresh interval has not elapsed
            Assert.Null(config["feature_management:feature_flags:4"]);
        }

        [Fact]
        public void OverwrittenRefreshIntervalForSameFeatureFlagRegistrations()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var refreshInterval1 = TimeSpan.FromSeconds(1);
            var refreshInterval2 = TimeSpan.FromSeconds(60);
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
                        ff.SetRefreshInterval(refreshInterval1);
                    });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select("*", "App1_Label");
                        ff.Select("*", "App2_Label");
                        ff.SetRefreshInterval(refreshInterval2);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("App1_Feature1", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:1:enabled"]);
            Assert.Equal("App1_Feature2", config["feature_management:feature_flags:1:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:2:enabled"]);
            Assert.Equal("Feature1", config["feature_management:feature_flags:2:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:3:enabled"]);
            Assert.Equal("App2_Feature1", config["feature_management:feature_flags:3:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:4:enabled"]);
            Assert.Equal("App2_Feature2", config["feature_management:feature_flags:4:id"]);

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

            Thread.Sleep(refreshInterval1);
            refresher.RefreshAsync().Wait();

            // The refresh interval time for feature flags was overwritten by second call to UseFeatureFlags.
            // Sleeping for refreshInterval1 time should not update feature flags.
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("App1_Feature1", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:1:enabled"]);
            Assert.Equal("App1_Feature2", config["feature_management:feature_flags:1:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:2:enabled"]);
            Assert.Equal("Feature1", config["feature_management:feature_flags:2:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:3:enabled"]);
            Assert.Equal("App2_Feature1", config["feature_management:feature_flags:3:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:4:enabled"]);
            Assert.Equal("App2_Feature2", config["feature_management:feature_flags:4:id"]);
        }

        [Fact]
        public void SelectAndRefreshSingleFeatureFlag()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var prefix1 = "Feature1";
            var label1 = "App1_Label";
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
                        ff.SetRefreshInterval(RefreshInterval);
                        ff.Select(prefix1, label1);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("False", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Feature1", config["feature_management:feature_flags:0:id"]);

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

            // Sleep to let the refresh interval for feature flag with label1 elapse
            Thread.Sleep(RefreshInterval);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Browser", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("Chrome", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["feature_management:feature_flags:0:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
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
                    options.UseFeatureFlags(o => o.SetRefreshInterval(RefreshInterval));
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("SuperUsers", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("MyFeature2", config["feature_management:feature_flags:0:id"]);

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

            Thread.Sleep(RefreshInterval);
            refresher.TryRefreshAsync().Wait();
            Assert.Equal("AllUsers", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("MyFeature", config["feature_management:feature_flags:0:id"]);
            Assert.Contains(LogHelper.BuildFeatureFlagReadMessage("myFeature1", null, TestHelpers.PrimaryConfigStoreEndpoint.ToString().TrimEnd('/')), verboseInvocation);
            Assert.Contains(LogHelper.BuildFeatureFlagUpdatedMessage("myFeature1"), informationalInvocation);

            featureFlags.RemoveAt(0);
            Thread.Sleep(RefreshInterval);
            refresher.TryRefreshAsync().Wait();

            Assert.Null(config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
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
                    options.UseFeatureFlags(o => o.SetRefreshInterval(RefreshInterval));
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetRefreshInterval(RefreshInterval);
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("SuperUsers", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            FirstKeyValue.Value = "newValue1";

            Thread.Sleep(RefreshInterval);
            refresher.TryRefreshAsync().Wait();
            Assert.Equal("SuperUsers", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
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
                            .SetRefreshInterval(RefreshInterval);
                    });
                    options.UseFeatureFlags(o => o.SetRefreshInterval(RefreshInterval));
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
            Assert.Equal("NoUsers", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
            Assert.Equal("MyFeature", config["feature_management:feature_flags:0:id"]);

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

            Thread.Sleep(RefreshInterval);
            refresher.TryRefreshAsync().Wait();

            Assert.Equal("newValue1", config["TestKey1"]);
            Assert.Equal("NoUsers", config["feature_management:feature_flags:0:conditions:client_filters:0:name"]);
        }

        [Fact]
        public void WithVariants()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_variantFeatureFlagCollection));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.UseFeatureFlags();
                })
                .Build();

            Assert.Equal("VariantsFeature1", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:0:enabled"]);
            Assert.Equal("Big", config["feature_management:feature_flags:0:variants:0:name"]);
            Assert.Equal("600px", config["feature_management:feature_flags:0:variants:0:configuration_value"]);
            Assert.Equal("Small", config["feature_management:feature_flags:0:variants:1:name"]);
            Assert.Equal("ShoppingCart:Small", config["feature_management:feature_flags:0:variants:1:configuration_reference"]);
            Assert.Equal("Disabled", config["feature_management:feature_flags:0:variants:1:status_override"]);
            Assert.Equal("Small", config["feature_management:feature_flags:0:allocation:default_when_disabled"]);
            Assert.Equal("Small", config["feature_management:feature_flags:0:allocation:default_when_enabled"]);
            Assert.Equal("Big", config["feature_management:feature_flags:0:allocation:user:0:variant"]);
            Assert.Equal("Marsha", config["feature_management:feature_flags:0:allocation:user:0:users:0"]);
            Assert.Equal("John", config["feature_management:feature_flags:0:allocation:user:0:users:1"]);
            Assert.Equal("Small", config["feature_management:feature_flags:0:allocation:user:1:variant"]);
            Assert.Equal("Alice", config["feature_management:feature_flags:0:allocation:user:1:users:0"]);
            Assert.Equal("Bob", config["feature_management:feature_flags:0:allocation:user:1:users:1"]);
            Assert.Equal("Big", config["feature_management:feature_flags:0:allocation:group:0:variant"]);
            Assert.Equal("Ring1", config["feature_management:feature_flags:0:allocation:group:0:groups:0"]);
            Assert.Equal("Small", config["feature_management:feature_flags:0:allocation:group:1:variant"]);
            Assert.Equal("Ring2", config["feature_management:feature_flags:0:allocation:group:1:groups:0"]);
            Assert.Equal("Ring3", config["feature_management:feature_flags:0:allocation:group:1:groups:1"]);
            Assert.Equal("Big", config["feature_management:feature_flags:0:allocation:percentile:0:variant"]);
            Assert.Equal("0", config["feature_management:feature_flags:0:allocation:percentile:0:from"]);
            Assert.Equal("50", config["feature_management:feature_flags:0:allocation:percentile:0:to"]);
            Assert.Equal("Small", config["feature_management:feature_flags:0:allocation:percentile:1:variant"]);
            Assert.Equal("50", config["feature_management:feature_flags:0:allocation:percentile:1:from"]);
            Assert.Equal("100", config["feature_management:feature_flags:0:allocation:percentile:1:to"]);
            Assert.Equal("13992821", config["feature_management:feature_flags:0:allocation:seed"]);

            Assert.Equal("VariantsFeature2", config["feature_management:feature_flags:1:id"]);
            Assert.Equal("False", config["feature_management:feature_flags:1:enabled"]);
            Assert.Equal("ObjectVariant", config["feature_management:feature_flags:1:variants:0:name"]);
            Assert.Equal("Value1", config["feature_management:feature_flags:1:variants:0:configuration_value:Key1"]);
            Assert.Equal("Value2", config["feature_management:feature_flags:1:variants:0:configuration_value:Key2:InsideKey2"]);
            Assert.Equal("NumberVariant", config["feature_management:feature_flags:1:variants:1:name"]);
            Assert.Equal("100", config["feature_management:feature_flags:1:variants:1:configuration_value"]);
            Assert.Equal("NullVariant", config["feature_management:feature_flags:1:variants:2:name"]);
            Assert.Equal("", config["feature_management:feature_flags:1:variants:2:configuration_value"]);
            Assert.True(config
                .GetSection("feature_management:feature_flags:1:variants:2")
                .AsEnumerable()
                .ToDictionary(x => x.Key, x => x.Value)
                .ContainsKey("feature_management:feature_flags:1:variants:2:configuration_value"));
            Assert.Equal("MissingValueVariant", config["feature_management:feature_flags:1:variants:3:name"]);
            Assert.Null(config["feature_management:feature_flags:1:variants:3:configuration_value"]);
            Assert.False(config
                .GetSection("feature_management:feature_flags:1:variants:3")
                .AsEnumerable()
                .ToDictionary(x => x.Key, x => x.Value)
                .ContainsKey("feature_management:feature_flags:1:variants:3:configuration_value"));
            Assert.Equal("BooleanVariant", config["feature_management:feature_flags:1:variants:4:name"]);
            Assert.Equal("True", config["feature_management:feature_flags:1:variants:4:configuration_value"]);
            Assert.Equal("ObjectVariant", config["feature_management:feature_flags:1:allocation:default_when_disabled"]);
            Assert.Equal("ObjectVariant", config["feature_management:feature_flags:1:allocation:default_when_enabled"]);

            Assert.Equal("VariantsFeature3", config["feature_management:feature_flags:2:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:2:enabled"]);
            Assert.Equal("NumberVariant", config["feature_management:feature_flags:2:allocation:default_when_enabled"]);
            Assert.Equal("1", config["feature_management:feature_flags:2:variants:0:configuration_value"]);
            Assert.Equal("2", config["feature_management:feature_flags:2:variants:1:configuration_value"]);
            Assert.Equal("Other", config["feature_management:feature_flags:2:variants:2:configuration_value"]);
            Assert.Equal("NumberVariant", config["feature_management:feature_flags:2:allocation:default_when_enabled"]);

            Assert.Equal("VariantsFeature4", config["feature_management:feature_flags:3:id"]);
            Assert.Equal("True", config["feature_management:feature_flags:3:enabled"]);
            Assert.Null(config["feature_management:feature_flags:3:variants"]);
            Assert.Null(config["feature_management:feature_flags:3:allocation"]);
        }

        [Fact]
        public void WithTelemetry()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_telemetryFeatureFlagCollection));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Connect(TestHelpers.PrimaryConfigStoreEndpoint, new DefaultAzureCredential());
                    options.UseFeatureFlags();
                })
                .Build();

            Assert.Equal("True", config["feature_management:feature_flags:0:telemetry:enabled"]);
            Assert.Equal("TelemetryFeature1", config["feature_management:feature_flags:0:id"]);
            Assert.Equal("Tag1Value", config["feature_management:feature_flags:0:telemetry:metadata:Tags.Tag1"]);
            Assert.Equal("Tag2Value", config["feature_management:feature_flags:0:telemetry:metadata:Tags.Tag2"]);
            Assert.Equal("c3c231fd-39a0-4cb6-3237-4614474b92c1", config["feature_management:feature_flags:0:telemetry:metadata:ETag"]);

            byte[] featureFlagIdHash;

            using (HashAlgorithm hashAlgorithm = SHA256.Create())
            {
                featureFlagIdHash = hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes($"{FeatureManagementConstants.FeatureFlagMarker}TelemetryFeature1\nlabel"));
            }

            string featureFlagId = Convert.ToBase64String(featureFlagIdHash)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            Assert.Equal(featureFlagId, config["feature_management:feature_flags:0:telemetry:metadata:FeatureFlagId"]);
            Assert.Equal($"{TestHelpers.PrimaryConfigStoreEndpoint}kv/{FeatureManagementConstants.FeatureFlagMarker}TelemetryFeature1?label=label", config["feature_management:feature_flags:0:telemetry:metadata:FeatureFlagReference"]);

            Assert.Equal("True", config["feature_management:feature_flags:1:telemetry:enabled"]);
            Assert.Equal("TelemetryFeature2", config["feature_management:feature_flags:1:id"]);
            Assert.Equal("Tag2Value", config["feature_management:feature_flags:1:telemetry:metadata:Tags.Tag1"]);
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
                CreateFeatureFlag("Feature_NoFilters", requirementType: "\"All\"", clientFiltersJsonString: emptyFilters),
                CreateFeatureFlag("Feature_RequireAll", requirementType: "\"All\"", clientFiltersJsonString: nonEmptyFilters),
                CreateFeatureFlag("Feature_RequireAny", requirementType: "\"Any\"", clientFiltersJsonString: nonEmptyFilters)
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

            Assert.Null(config["feature_management:feature_flags:0:requirement_type"]);
            Assert.Equal("MyFeature2", config["feature_management:feature_flags:0:id"]);
            Assert.Null(config["feature_management:feature_flags:1:requirement_type"]);
            Assert.Equal("Feature_NoFilters", config["feature_management:feature_flags:1:id"]);
            Assert.Equal("All", config["feature_management:feature_flags:2:conditions:requirement_type"]);
            Assert.Equal("Feature_RequireAll", config["feature_management:feature_flags:2:id"]);
            Assert.Equal("Any", config["feature_management:feature_flags:3:conditions:requirement_type"]);
            Assert.Equal("Feature_RequireAny", config["feature_management:feature_flags:3:id"]);
        }

        [Fact]
        public void ThrowsOnIncorrectJsonTypes()
        {
            var settings = new List<ConfigurationSetting>()
            {
                CreateFeatureFlag("Feature1", variantsJsonString: @"[{""name"": 1}]"),
                CreateFeatureFlag("Feature2", variantsJsonString: @"[{""configuration_reference"": true}]"),
                CreateFeatureFlag("Feature3", variantsJsonString: @"[{""status_override"": []}]"),
                CreateFeatureFlag("Feature4", seed: "{}"),
                CreateFeatureFlag("Feature5", defaultWhenDisabled: "5"),
                CreateFeatureFlag("Feature6", defaultWhenEnabled: "6"),
                CreateFeatureFlag("Feature7", userJsonString: @"[{""variant"": []}]"),
                CreateFeatureFlag("Feature8", userJsonString: @"[{""users"": [ {""name"": ""8""} ]}]"),
                CreateFeatureFlag("Feature9", groupJsonString: @"[{""variant"": false}]"),
                CreateFeatureFlag("Feature10", groupJsonString: @"[{""groups"": 10}]"),
                CreateFeatureFlag("Feature11", percentileJsonString: @"[{""variant"": []}]"),
                CreateFeatureFlag("Feature12", percentileJsonString: @"[{""from"": true}]"),
                CreateFeatureFlag("Feature13", percentileJsonString: @"[{""to"": {}}]"),
                CreateFeatureFlag("Feature14", telemetryEnabled: "14"),
                CreateFeatureFlag("Feature15", telemetryMetadataJsonString: @"{""key"": 15}"),
                CreateFeatureFlag("Feature16", clientFiltersJsonString: @"[{""name"": 16}]"),
                CreateFeatureFlag("Feature17", clientFiltersJsonString: @"{""key"": [{""name"": ""name"", ""parameters"": 17}]}"),
                CreateFeatureFlag("Feature18", requirementType: "18")
            };

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            foreach (ConfigurationSetting setting in settings)
            {
                var featureFlags = new List<ConfigurationSetting> { setting };

                mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                    .Returns(new MockAsyncPageable(featureFlags));

                void action() => new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                        options.UseFeatureFlags();
                    }).Build();

                var exception = Assert.Throws<FormatException>(action);

                Assert.False(exception.InnerException is JsonException);
            }
        }

        Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
        {
            return Response.FromValue(FirstKeyValue, new MockResponse(200));
        }

        Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
        {
            return Response.FromValue(TestHelpers.CloneSetting(FirstKeyValue), new Mock<Response>().Object);
        }

        private ConfigurationSetting CreateFeatureFlag(string featureId,
            string requirementType = "null",
            string clientFiltersJsonString = "null",
            string variantsJsonString = "null",
            string seed = "null",
            string defaultWhenDisabled = "null",
            string defaultWhenEnabled = "null",
            string userJsonString = "null",
            string groupJsonString = "null",
            string percentileJsonString = "null",
            string telemetryEnabled = "null",
            string telemetryMetadataJsonString = "null")
        {
            return ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + featureId,
                value: $@"
                        {{
                            ""id"": ""{featureId}"",
                            ""enabled"": true,
                            ""conditions"": {{
                              ""requirement_type"": {requirementType},
                              ""client_filters"": {clientFiltersJsonString}
                            }},
                            ""variants"": {variantsJsonString},
	                        ""allocation"": {{
		                        ""seed"": {seed},
		                        ""default_when_disabled"": {defaultWhenDisabled},
		                        ""default_when_enabled"": {defaultWhenEnabled},
		                        ""user"": {userJsonString},
		                        ""group"": {groupJsonString},
		                        ""percentile"": {percentileJsonString}
	                        }},
                            ""telemetry"": {{
                                ""enabled"": {telemetryEnabled},
                                ""metadata"": {telemetryMetadataJsonString}
                            }}
                        }}
                        ",
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"));
        }
    }
}
