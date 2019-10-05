using Azure;
using Azure.Core.Http;
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
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());

            var featureFlags = new List<ConfigurationSetting> { _kv };

            mockClient.Setup(c => c.GetSettingsAsync(new SettingSelector("*", LabelFilter.Null), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var testClient = mockClient.Object;

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

        [Fact]
        public void WatchesFeatureFlags()
        {
            var mockResponse = new Mock<Response>();
            var mock = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            var featureFlags = new List<ConfigurationSetting> { _kv };

            mock.Setup(c => c.GetSettingsAsync(new SettingSelector("*", LabelFilter.Null), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(featureFlags));

            var testClient = mock.Object;
            var builder = new ConfigurationBuilder();

            var options = new AzureAppConfigurationOptions { Client = testClient }
                .UseFeatureFlags(o => o.CacheExpirationTime = TimeSpan.FromSeconds(1));

            var config = builder
                .AddAzureAppConfiguration(options)
                .Build();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Firefox", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("RollOut", config["FeatureManagement:Beta:EnabledFor:1:Name"]);
            Assert.Equal("20", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Percentage"]);
            Assert.Equal("US", config["FeatureManagement:Beta:EnabledFor:1:Parameters:Region"]);
            Assert.Equal("SuperUsers", config["FeatureManagement:Beta:EnabledFor:2:Name"]);

            _kv = ConfigurationModelFactory.ConfigurationSetting(
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

            var mockResponse2 = new Mock<Response>();
            featureFlags.Add(_kv2);
            options.GetRefresher().Refresh();

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

            var client = new ConfigurationClient(TestHelpers.CreateMockEndpointString(), clientOptions);

            //
            // Test scenario
            var builder = new ConfigurationBuilder();
            var options = new AzureAppConfigurationOptions()
            {
                Client = client
            };

            options.UseFeatureFlags();
            builder.AddAzureAppConfiguration(options);
            var config = builder.Build();

            // TODO (Pavel): Can we get the request pre-escaped?
            MockRequest request = mockTransport.SingleRequest;

            Assert.Contains("/kv/?key=*&label=%00", Uri.EscapeUriString(request.Uri.PathAndQuery));
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

            var client = new ConfigurationClient(TestHelpers.CreateMockEndpointString(), clientOptions);

            //
            // Test scenario
            var builder = new ConfigurationBuilder();
            var options = new AzureAppConfigurationOptions()
            {
                Client = client
            };

            options.UseFeatureFlags(o => o.Label = "myLabel");
            builder.AddAzureAppConfiguration(options);
            var config = builder.Build();

            bool performedDefaultQuery = mockTransport.Requests.Any(r => Uri.EscapeUriString(r.Uri.PathAndQuery).Contains("/kv/?key=*&label=%00"));
            bool queriedFeatureFlags = mockTransport.Requests.Any(r => Uri.EscapeDataString(r.Uri.PathAndQuery).Contains(Uri.EscapeDataString(FeatureManagementConstants.FeatureFlagMarker)));

            Assert.True(performedDefaultQuery);
            Assert.True(queriedFeatureFlags);
        }
    }
}
