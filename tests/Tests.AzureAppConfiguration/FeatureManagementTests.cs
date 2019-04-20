using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class FeatureManagementTests
    {
        private KeyValue _kv = new KeyValue(FeatureManagementConstants.FeatureFlagMarker + "myFeature")
        {
            Value = @"
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
                          }
                        ]
                      }
                    }
                    ",
            ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c1",
            ContentType = FeatureManagementConstants.ContentType
        };

        private KeyValue _kv2 = new KeyValue(FeatureManagementConstants.FeatureFlagMarker + "myFeature2")
        {
            Value = @"
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
            ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c1",
            ContentType = FeatureManagementConstants.ContentType
        };

        [Fact]
        public void UsesFeatureFlags()
        {
            IEnumerable<IKeyValue> featureFlags = new List<IKeyValue> { _kv };

            using (var testClient = new AzconfigClient(TestHelpers.CreateMockEndpointString(), new MockedGetKeyValueRequest(_kv, featureFlags)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                options.UseFeatureFlags();

                builder.AddAzureAppConfiguration(options);

                var config = builder.Build();

                Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
                Assert.Equal("Firefox", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
                Assert.Equal("Safari", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
                Assert.Equal("RollOut", config["FeatureManagement:Beta:EnabledFor:1:Name"]);
                Assert.Equal("20", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Percentage"]);
                Assert.Equal("US", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Region"]);
                Assert.Equal("SuperUsers", config["FeatureManagement:Beta:EnabledFor:2:Name"]);
            }
        }

        [Fact]
        public void WatchesFeatureFlags()
        {
            List<IKeyValue> featureFlags = new List<IKeyValue> { _kv };

            using (var testClient = new AzconfigClient(TestHelpers.CreateMockEndpointString(), new MockedGetKeyValueRequest(_kv, featureFlags)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                options.UseFeatureFlags(o => o.PollInterval = TimeSpan.FromMilliseconds(500));

                builder.AddAzureAppConfiguration(options);

                var config = builder.Build();

                Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
                Assert.Equal("Firefox", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
                Assert.Equal("Safari", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
                Assert.Equal("RollOut", config["FeatureManagement:Beta:EnabledFor:1:Name"]);
                Assert.Equal("20", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Percentage"]);
                Assert.Equal("US", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Region"]);
                Assert.Equal("SuperUsers", config["FeatureManagement:Beta:EnabledFor:2:Name"]);

                _kv.Value = @"
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
                    ";

                _kv.ETag += "f";

                featureFlags.Add(_kv2);

                Task.Delay(1500).Wait();

                Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
                Assert.Equal("Chrome", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
                Assert.Equal("Edge", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
                Assert.Equal("SuperUsers", config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
            }
        }
    }
}
