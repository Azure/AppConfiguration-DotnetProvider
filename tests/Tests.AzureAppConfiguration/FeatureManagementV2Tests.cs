// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
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
    public class FeatureManagementV2Tests
    {
        private static ConfigurationSetting _ff1 = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "Beta",
            value: @"
                    {
                      ""id"": ""Beta"",
                      ""description"": ""The new beta version of our web site."",
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

        private static ConfigurationSetting _ff2 = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "MyFeature",
            value: @"
                    {
                      ""id"": ""MyFeature"",
                      ""description"": ""The new beta version of our web site."",
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

        private static ConfigurationSetting _df1 = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "ShoppingCart",
            value: @"
                    {
                      ""id"": ""ShoppingCart"",
                      ""description"": """",
                      ""client_assigner"": ""Microsoft.Targeting"",
                      ""variants"": [
                            {
                                ""default"": true,
                                ""name"": ""Big"",
                                ""configuration_reference"": ""ShoppingCart:Big"",
                                ""assignment_parameters"": {
                                    ""Audience"": {
                                        ""Users"": [
                                            ""Alec""
                                        ],
                                        ""Groups"": [
                                        ]
                                    }
                                }
                            },
                            {
                                ""name"": ""Small"",
                                ""configuration_reference"": ""ShoppingCart:Small"",
                                ""assignment_parameters"": {
                                    ""Audience"": {
                                        ""Users"": [
                                        ],
                                        ""Groups"": [
                                            {
                                                ""Name"": ""Ring1"",
                                                ""RolloutPercentage"": 50
                                            }
                                        ],
                                        ""DefaultRolloutPercentage"": 30
                                    }
                                }
                            }
                        ]
                    }
                    ",
            label: default,
            contentType: FeatureManagementConstants.DynamicFeatureContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"));

        private static ConfigurationSetting _df2 = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "DiscountBanner",
            value: @"
                    {
                      ""id"": ""DiscountBanner"",
                      ""description"": """",
                      ""client_assigner"": ""Targeting"",
                      ""variants"": [
                            {
                                ""default"": true,
                                ""name"": ""Big"",
                                ""configuration_reference"": ""DiscountBanner:Big""
                            },
                            {
                                ""name"": ""Small"",
                                ""configuration_reference"": ""DiscountBanner:Small"",
                                ""assignment_parameters"": {
                                    ""Audience"": {
                                        ""Users"": [
                                            ""Jeff"",
                                            ""Alicia""
                                        ],
                                        ""Groups"": [
                                            {
                                                ""Name"": ""Ring0"",
                                                ""RolloutPercentage"": 80
                                            },
                                            {
                                                ""Name"": ""Ring1"",
                                                ""RolloutPercentage"": 50
                                            }
                                        ],
                                        ""DefaultRolloutPercentage"": 20
                                    }
                                }
                            }
                        ]
                      }
                    ",
            label: default,
            contentType: FeatureManagementConstants.DynamicFeatureContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"));

        List<ConfigurationSetting> _featureManagementCollection = new List<ConfigurationSetting> { _ff1, _ff2, _df1, _df2 };

        [Fact]
        public void UsesFeatureManagementV2Schema()
        {
            // Set environment variable to choose v2 schema
            Environment.SetEnvironmentVariable(FeatureManagementConstants.FeatureManagementSchemaEnvironmentVariable, FeatureManagementConstants.FeatureManagementSchemaV2);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            var featureFlags = new List<ConfigurationSetting> { _ff1, _df1 };

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.UseFeatureFlags();
                })
                .Build();

            Assert.Equal("Browser", config["FeatureManagement:FeatureFlags:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Firefox", config["FeatureManagement:FeatureFlags:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:FeatureFlags:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["FeatureManagement:FeatureFlags:Beta:EnabledFor:1:Name"]);
            Assert.Equal("20", config["FeatureManagement:FeatureFlags:Beta:EnabledFor:1:Parameters:Percentage"]);
            Assert.Equal("US", config["FeatureManagement:FeatureFlags:Beta:EnabledFor:1:Parameters:Region"]);
            Assert.Equal("SuperUsers", config["FeatureManagement:FeatureFlags:Beta:EnabledFor:2:Name"]);
            Assert.Equal("TimeWindow", config["FeatureManagement:FeatureFlags:Beta:EnabledFor:3:Name"]);
            Assert.Equal("/Date(1578643200000)/", config["FeatureManagement:FeatureFlags:Beta:EnabledFor:3:Parameters:Start"]);
            Assert.Equal("/Date(1578686400000)/", config["FeatureManagement:FeatureFlags:Beta:EnabledFor:3:Parameters:End"]);

            Assert.Equal("Microsoft.Targeting", config["FeatureManagement:DynamicFeatures:ShoppingCart:Assigner"]);
            Assert.Equal("Alec", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:AssignmentParameters:Audience:Users:0"]);
            Assert.Equal("ShoppingCart:Big", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:ConfigurationReference"]);
            Assert.Equal("True", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:Default"]);
            Assert.Equal("Big", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:Name"]);
            Assert.Equal("30", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:AssignmentParameters:Audience:DefaultRolloutPercentage"]);
            Assert.Equal("Ring1", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:AssignmentParameters:Audience:Groups:0:Name"]);
            Assert.Equal("50", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:AssignmentParameters:Audience:Groups:0:RolloutPercentage"]);
            Assert.Equal("ShoppingCart:Small", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:ConfigurationReference"]);
            Assert.Equal("Small", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:Name"]);

            // Delete the environment variable
            Environment.SetEnvironmentVariable(FeatureManagementConstants.FeatureManagementSchemaEnvironmentVariable, null);
        }

        [Fact]
        public void UsesDefaultSchemaIfNoEnvironmentVariable()
        {
            Environment.SetEnvironmentVariable(FeatureManagementConstants.FeatureManagementSchemaEnvironmentVariable, null);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            var featureFlags = new List<ConfigurationSetting> { _ff1, _df1 };

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
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

            // Dynamic feature will be treated as a regular key-value
            Assert.NotNull(config[FeatureManagementConstants.FeatureFlagMarker + "ShoppingCart"]);
            Assert.Contains("\"client_assigner\": \"Microsoft.Targeting\"", config[FeatureManagementConstants.FeatureFlagMarker + "ShoppingCart"]);
            Assert.Null(config["FeatureManagement:DynamicFeatures:ShoppingCart:Assigner"]);
        }

        [Fact]
        public void UsesDefaultSchemaIfInvalidEnvironmentVariable()
        {
            Environment.SetEnvironmentVariable(FeatureManagementConstants.FeatureManagementSchemaEnvironmentVariable, "3");

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            var featureFlags = new List<ConfigurationSetting> { _ff1, _df1 };

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
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

            // Dynamic feature will be treated as a regular key-value
            Assert.NotNull(config[FeatureManagementConstants.FeatureFlagMarker + "ShoppingCart"]);
            Assert.Contains("\"client_assigner\": \"Microsoft.Targeting\"", config[FeatureManagementConstants.FeatureFlagMarker + "ShoppingCart"]);
            Assert.Null(config["FeatureManagement:DynamicFeatures:ShoppingCart:Assigner"]);
        }

        [Fact]
        public void WatchesDynamicFeatures()
        {
            // Set environment variable to choose v2 schema
            Environment.SetEnvironmentVariable(FeatureManagementConstants.FeatureManagementSchemaEnvironmentVariable, FeatureManagementConstants.FeatureManagementSchemaV2);

            var dynamicFeatures = new List<ConfigurationSetting> { _df1 };

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(dynamicFeatures));

            IConfigurationRefresher refresher = null;
            var cacheExpirationTimeSpan = TimeSpan.FromSeconds(1);
            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                    options.UseFeatureFlags(o =>
                    {
                        o.CacheExpirationInterval = cacheExpirationTimeSpan;
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Microsoft.Targeting", config["FeatureManagement:DynamicFeatures:ShoppingCart:Assigner"]);
            Assert.Equal("Alec", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:AssignmentParameters:Audience:Users:0"]);
            Assert.Equal("ShoppingCart:Big", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:ConfigurationReference"]);
            Assert.Equal("True", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:Default"]);
            Assert.Equal("Big", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:Name"]);
            Assert.Equal("30", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:AssignmentParameters:Audience:DefaultRolloutPercentage"]);
            Assert.Equal("Ring1", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:AssignmentParameters:Audience:Groups:0:Name"]);
            Assert.Equal("50", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:AssignmentParameters:Audience:Groups:0:RolloutPercentage"]);
            Assert.Equal("ShoppingCart:Small", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:ConfigurationReference"]);
            Assert.Equal("Small", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:Name"]);

            Assert.Null(config["FeatureManagement:DynamicFeatures:DiscountBanner:Assigner"]);

            dynamicFeatures[0] = ConfigurationModelFactory.ConfigurationSetting(
            key: FeatureManagementConstants.FeatureFlagMarker + "ShoppingCart",
            value: @"
                    {
                      ""id"": ""ShoppingCart"",
                      ""description"": """",
                      ""client_assigner"": ""Microsoft.Targeting"",
                      ""variants"": [
                            {
                                ""default"": true,
                                ""name"": ""Big"",
                                ""configuration_reference"": ""ShoppingCart:Big"",
                                ""assignment_parameters"": {
                                    ""Audience"": {
                                        ""Users"": [
                                            ""Bob""
                                        ],
                                        ""Groups"": [
                                        ]
                                    }
                                }
                            },
                            {
                                ""name"": ""Small"",
                                ""configuration_reference"": ""ShoppingCart:Small"",
                                ""assignment_parameters"": {
                                    ""Audience"": {
                                        ""Users"": [
                                        ],
                                        ""Groups"": [
                                            {
                                                ""Name"": ""Ring1"",
                                                ""RolloutPercentage"": 70
                                            }
                                        ],
                                        ""DefaultRolloutPercentage"": 30
                                    }
                                }
                            }
                        ]
                    }
                    ",
            label: default,
            contentType: FeatureManagementConstants.DynamicFeatureContentType + ";charset=utf-8",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1" + "f"));

            dynamicFeatures.Add(_df2);

            // Sleep to let the cache expire
            Thread.Sleep(cacheExpirationTimeSpan);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Bob", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:AssignmentParameters:Audience:Users:0"]);
            Assert.Equal("70", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:AssignmentParameters:Audience:Groups:0:RolloutPercentage"]);

            Assert.Equal("Targeting", config["FeatureManagement:DynamicFeatures:DiscountBanner:Assigner"]);
            Assert.Equal("DiscountBanner:Big", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:0:ConfigurationReference"]);
            Assert.Equal("True", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:0:Default"]);
            Assert.Equal("Big", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:0:Name"]);
            Assert.Equal("20", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:1:AssignmentParameters:Audience:DefaultRolloutPercentage"]);
            Assert.Equal("Ring0", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:1:AssignmentParameters:Audience:Groups:0:Name"]);
            Assert.Equal("80", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:1:AssignmentParameters:Audience:Groups:0:RolloutPercentage"]);
            Assert.Equal("Ring1", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:1:AssignmentParameters:Audience:Groups:1:Name"]);
            Assert.Equal("50", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:1:AssignmentParameters:Audience:Groups:1:RolloutPercentage"]);
            Assert.Equal("Jeff", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:1:AssignmentParameters:Audience:Users:0"]);
            Assert.Equal("Alicia", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:1:AssignmentParameters:Audience:Users:1"]);
            Assert.Equal("DiscountBanner:Small", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:1:ConfigurationReference"]);
            Assert.Equal("Small", config["FeatureManagement:DynamicFeatures:DiscountBanner:Variants:1:Name"]);

            // Delete the environment variable
            Environment.SetEnvironmentVariable(FeatureManagementConstants.FeatureManagementSchemaEnvironmentVariable, null);
        }

        [Fact]
        public void SelectsDynamicFeatures()
        {
            // Set environment variable to choose v2 schema
            Environment.SetEnvironmentVariable(FeatureManagementConstants.FeatureManagementSchemaEnvironmentVariable, FeatureManagementConstants.FeatureManagementSchemaV2);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            var featureFlagPrefix = "Shopping";

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_featureManagementCollection.Where(s => s.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + featureFlagPrefix)).ToList()));

            var testClient = mockClient.Object;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(testClient);
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(featureFlagPrefix + "*");
                    });
                })
                .Build();

            Assert.Equal("Microsoft.Targeting", config["FeatureManagement:DynamicFeatures:ShoppingCart:Assigner"]);
            Assert.Equal("Alec", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:AssignmentParameters:Audience:Users:0"]);
            Assert.Equal("ShoppingCart:Big", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:ConfigurationReference"]);
            Assert.Equal("True", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:Default"]);
            Assert.Equal("Big", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:0:Name"]);
            Assert.Equal("30", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:AssignmentParameters:Audience:DefaultRolloutPercentage"]);
            Assert.Equal("Ring1", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:AssignmentParameters:Audience:Groups:0:Name"]);
            Assert.Equal("50", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:AssignmentParameters:Audience:Groups:0:RolloutPercentage"]);
            Assert.Equal("ShoppingCart:Small", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:ConfigurationReference"]);
            Assert.Equal("Small", config["FeatureManagement:DynamicFeatures:ShoppingCart:Variants:1:Name"]);

            // Verify that the feature flags and dynamic features that did not start with the specified prefix was not loaded
            Assert.Null(config["FeatureManagement:FeatureFlags:Beta"]);
            Assert.Null(config["FeatureManagement:FeatureFlags:MyFeature"]);
            Assert.Null(config["FeatureManagement:DynamicFeatures:DiscountBanner:Assigner"]);

            // Delete the environment variable
            Environment.SetEnvironmentVariable(FeatureManagementConstants.FeatureManagementSchemaEnvironmentVariable, null);
        }
    }
}
