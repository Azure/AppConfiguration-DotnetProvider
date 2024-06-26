﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Diagnostics;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Data.AppConfiguration.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Moq;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class FeatureManagementTests
    {
        private readonly ConfigurationSetting _kv = ConfigurationModelFactory.ConfigurationSetting(
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

        private readonly ConfigurationSetting _kv2 = ConfigurationModelFactory.ConfigurationSetting(
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
        private readonly List<ConfigurationSetting> _nullOrMissingConditionsFeatureFlagCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "NullParameters",
            value: @"
                            {
                              ""id"": ""NullParameters"",
                              ""description"": """",
                              ""display_name"": ""Null Parameters"",
                              ""enabled"": true,
                              ""conditions"": {
                                ""client_filters"": [
                                  {
                                    ""name"": ""Filter"",
                                    ""parameters"": null
                                  }
                                ]
                              }
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "NullConditions",
            value: @"
                            {
                              ""id"": ""NullConditions"",
                              ""description"": """",
                              ""display_name"": ""Null Conditions"",
                              ""enabled"": true,
                              ""conditions"": null
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "NullClientFilters",
            value: @"
                            {
                              ""id"": ""NullClientFilters"",
                              ""description"": """",
                              ""display_name"": ""Null Client Filters"",
                              ""enabled"": true,
                              ""conditions"": {
                                ""client_filters"": null
                              }
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "NoConditions",
            value: @"
                            {
                              ""id"": ""NoConditions"",
                              ""description"": """",
                              ""display_name"": ""No Conditions"",
                              ""enabled"": true
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "EmptyConditions",
            value: @"
                            {
                              ""id"": ""EmptyConditions"",
                              ""description"": """",
                              ""display_name"": ""Empty Conditions"",
                              ""conditions"": {},
                              ""enabled"": true
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "EmptyClientFilter",
            value: @"
                            {
                              ""id"": ""EmptyClientFilter"",
                              ""description"": """",
                              ""display_name"": ""Empty Client Filter"",
                              ""conditions"": {
                                ""client_filters"": [
                                    {}
                                ]
                              },
                              ""enabled"": true
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"))
        };

        private readonly List<ConfigurationSetting> _validFormatFeatureFlagCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "AdditionalProperty",
            value: @"
                            {
                              ""id"": ""AdditionalProperty"",
                              ""description"": ""Should not throw an exception, additional properties are skipped."",
                              ""ignored_object"": {
                                ""id"": false
                              },
                              ""enabled"": true,
                              ""conditions"": {}
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "DuplicateProperty",
            value: @"
                            {
                              ""id"": ""DuplicateProperty"",
                              ""description"": ""Should not throw an exception, last of duplicate properties will win."",
                              ""enabled"": false,
                              ""enabled"": true,
                              ""conditions"": {}
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "AllowNullRequirementType",
            value: @"
                            {
                              ""id"": ""AllowNullRequirementType"",
                              ""description"": ""Should not throw an exception, requirement type is allowed as null."",
                              ""enabled"": true,
                              ""conditions"": {
                                ""requirement_type"": null
                              }
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"))
        };
        private readonly List<ConfigurationSetting> _invalidFormatFeatureFlagCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "MissingClosingBracket1",
            value: @"
                            {
                              ""id"": ""MissingClosingBracket1"",
                              ""description"": ""Should throw an exception, invalid end of json."",
                              ""enabled"": true,
                              ""conditions"": {}
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "MissingClosingBracket2",
            value: @"
                            {
                              ""id"": ""MissingClosingBracket2"",
                              ""description"": ""Should throw an exception, invalid end of conditions object."",
                              ""conditions"": {,
                              ""enabled"": true
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "MissingClosingBracket3",
            value: @"
                            {
                              ""id"": ""MissingClosingBracket3"",
                              ""description"": ""Should throw an exception, no closing bracket on client filters array."",
                              ""conditions"": {
                                ""client_filters"": [
                              },
                              ""enabled"": true
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "MissingOpeningBracket1",
            value: @"
                            {
                              ""id"": ""MissingOpeningBracket1"",
                              ""description"": ""Should throw an exception, no opening bracket on conditions object."",
                              ""conditions"": },
                              ""enabled"": true
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1")),

            ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "MissingOpeningBracket2",
            value: @"
                            {
                              ""id"": ""MissingOpeningBracket2"",
                              ""description"": ""Should throw an exception, no opening bracket on client filters array."",
                              ""conditions"": {
                                ""client_filters"": ]
                              },
                              ""enabled"": true
                            }
                            ",
            label: default,
            contentType: FeatureManagementConstants.ContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"))
        };
        private readonly List<ConfigurationSetting> _featureFlagCollection = new List<ConfigurationSetting>
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
        private readonly ConfigurationSetting FirstKeyValue = ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey1",
                label: "label",
                value: "TestValue1",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType: "text");
        private readonly TimeSpan CacheExpirationTime = TimeSpan.FromSeconds(1);

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
        public async Task WatchesFeatureFlags()
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
            await refresher.RefreshAsync();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Chrome", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("SuperUsers", config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
        }

        [Fact]
        public async Task SkipRefreshIfCacheNotExpired()
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

            await refresher.RefreshAsync();

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
        public async Task UsesEtagForFeatureFlagRefresh()
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

            await refresher.TryRefreshAsync();
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
        public void TestNullAndMissingValuesForConditions()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var cacheExpiration = TimeSpan.FromSeconds(1);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_nullOrMissingConditionsFeatureFlagCollection));

            var testClient = mockClient.Object;

            // Makes sure that adapter properly processes values and doesn't throw an exception
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.CacheExpirationInterval = cacheExpiration;
                        ff.Select(KeyFilter.Any);
                    });
                })
                .Build();

            Assert.Null(config["FeatureManagement:NullConditions:EnabledFor"]);
            Assert.Equal("Filter", config["FeatureManagement:NullParameters:EnabledFor:0:Name"]);
            Assert.Null(config["FeatureManagement:NullParameters:EnabledFor:0:Parameters"]);
            Assert.Null(config["FeatureManagement:NullClientFilters:EnabledFor"]);
            Assert.Null(config["FeatureManagement:NoConditions:EnabledFor"]);
            Assert.Null(config["FeatureManagement:EmptyConditions:EnabledFor"]);
            Assert.Null(config["FeatureManagement:EmptyClientFilter:EnabledFor"]);
        }

        [Fact]
        public void InvalidFeatureFlagFormatsThrowFormatException()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var cacheExpiration = TimeSpan.FromSeconds(1);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns((Func<SettingSelector, CancellationToken, MockAsyncPageable>)GetTestKeys);

            MockAsyncPageable GetTestKeys(SettingSelector selector, CancellationToken ct)
            {
                var copy = new List<ConfigurationSetting>();
                var newSetting = _invalidFormatFeatureFlagCollection.FirstOrDefault(s => s.Key == selector.KeyFilter);
                if (newSetting != null)
                    copy.Add(TestHelpers.CloneSetting(newSetting));
                return new MockAsyncPageable(copy);
            };

            var testClient = mockClient.Object;

            foreach (ConfigurationSetting setting in _invalidFormatFeatureFlagCollection)
            {
                void action() => new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Select("_");
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.CacheExpirationInterval = cacheExpiration;
                        ff.Select(setting.Key.Substring(FeatureManagementConstants.FeatureFlagMarker.Length));
                    });
                })
                .Build();

                // Each of the feature flags should throw an exception
                Assert.Throws<FormatException>(action);
            }
        }

        [Fact]
        public void AlternateValidFeatureFlagFormats()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var cacheExpiration = TimeSpan.FromSeconds(1);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns((Func<SettingSelector, CancellationToken, MockAsyncPageable>)GetTestKeys);

            MockAsyncPageable GetTestKeys(SettingSelector selector, CancellationToken ct)
            {
                var copy = new List<ConfigurationSetting>();
                var newSetting = _validFormatFeatureFlagCollection.FirstOrDefault(s => s.Key == selector.KeyFilter);
                if (newSetting != null)
                    copy.Add(TestHelpers.CloneSetting(newSetting));
                return new MockAsyncPageable(copy);
            };

            var testClient = mockClient.Object;

            foreach (ConfigurationSetting setting in _validFormatFeatureFlagCollection)
            {
                string flagKey = setting.Key.Substring(FeatureManagementConstants.FeatureFlagMarker.Length);

                var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Select("_");
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.CacheExpirationInterval = cacheExpiration;
                        ff.Select(flagKey);
                    });
                })
                .Build();

                // None of the feature flags should throw an exception, and the flag should be loaded like normal
                Assert.Equal("True", config[$"FeatureManagement:{flagKey}"]);
            }
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
        public async Task DifferentCacheExpirationsForMultipleFeatureFlagRegistrations()
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
            await refresher.RefreshAsync();

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
        public async Task OverwrittenCacheExpirationForSameFeatureFlagRegistrations()
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
            await refresher.RefreshAsync();

            // The cache expiration time for feature flags was overwritten by second call to UseFeatureFlags.
            // Sleeping for cacheExpiration1 time should not update feature flags.
            Assert.Equal("True", config["FeatureManagement:App1_Feature1"]);
            Assert.Equal("False", config["FeatureManagement:App1_Feature2"]);
            Assert.Equal("False", config["FeatureManagement:App2_Feature1"]);
            Assert.Equal("True", config["FeatureManagement:App2_Feature2"]);
            Assert.Equal("True", config["FeatureManagement:Feature1"]);
        }

        [Fact]
        public async Task SelectAndRefreshSingleFeatureFlag()
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
            await refresher.RefreshAsync();

            Assert.Equal("Browser", config["FeatureManagement:Feature1:EnabledFor:0:Name"]);
            Assert.Equal("Chrome", config["FeatureManagement:Feature1:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["FeatureManagement:Feature1:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
        }

        [Fact]
        public async Task ValidateCorrectFeatureFlagLoggedIfModifiedOrRemovedDuringRefresh()
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
            await refresher.TryRefreshAsync();
            Assert.Equal("AllUsers", config["FeatureManagement:MyFeature:EnabledFor:0:Name"]);
            Assert.Contains(LogHelper.BuildFeatureFlagReadMessage("myFeature1", null, TestHelpers.PrimaryConfigStoreEndpoint.ToString().TrimEnd('/')), verboseInvocation);
            Assert.Contains(LogHelper.BuildFeatureFlagUpdatedMessage("myFeature1"), informationalInvocation);

            featureFlags.RemoveAt(0);
            Thread.Sleep(CacheExpirationTime);
            await refresher.TryRefreshAsync();

            Assert.Null(config["FeatureManagement:MyFeature:EnabledFor:0:Name"]);
            Assert.Contains(LogHelper.BuildFeatureFlagReadMessage("myFeature1", null, TestHelpers.PrimaryConfigStoreEndpoint.ToString().TrimEnd('/')), verboseInvocation);
            Assert.Contains(LogHelper.BuildFeatureFlagUpdatedMessage("myFeature1"), informationalInvocation);
        }

        [Fact]
        public async Task ValidateFeatureFlagsUnchangedLogged()
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
            await refresher.TryRefreshAsync();
            Assert.Equal("SuperUsers", config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
            Assert.Contains(LogHelper.BuildFeatureFlagsUnchangedMessage(TestHelpers.PrimaryConfigStoreEndpoint.ToString().TrimEnd('/')), verboseInvocation);
        }

        [Fact]
        public async Task MapTransformFeatureFlagWithRefresh()
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
            await refresher.TryRefreshAsync();

            Assert.Equal("newValue1", config["TestKey1"]);
            Assert.Equal("NoUsers", config["FeatureManagement:MyFeature:EnabledFor:0:Name"]);
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

        private Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
        {
            return Response.FromValue(FirstKeyValue, new MockResponse(200));
        }

        private Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
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
