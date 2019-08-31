using Azure;
using Azure.Core.Http;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class FeatureManagementTests
    {
        private ConfigurationSetting _kv = new ConfigurationSetting(FeatureManagementConstants.FeatureFlagMarker + "myFeature",
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
                    ")
        {
            ETag = new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
            ContentType = FeatureManagementConstants.ContentType + ";charset=utf-8"
        };

        private ConfigurationSetting _kv2 = new ConfigurationSetting(FeatureManagementConstants.FeatureFlagMarker + "myFeature2",
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
                    ")
        {
            ETag = new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
            ContentType = FeatureManagementConstants.ContentType + ";charset=utf-8"
        };

        internal class MockAsyncCollection : AsyncCollection<ConfigurationSetting>
        {
            public override IAsyncEnumerable<Page<ConfigurationSetting>> ByPage(string continuationToken = null, int? pageSizeHint = null)
            {
                throw new NotImplementedException();
            }
        }

        [Fact]
        public void UsesFeatureFlags()
        {
            var mockResponse = new Mock<Response>();
            var mock = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());

            var featureFlags = new List<Response<ConfigurationSetting>>
            {
                new Response<ConfigurationSetting>(mockResponse.Object, _kv)
            };

            mock.Setup(c => c.GetSettingsAsync(new SettingSelector(), It.IsAny<CancellationToken>()))
                .Returns(new List<Response<ConfigurationSetting>>(featureFlags));

            var testClient = mock.Object;

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
            var mock = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());

            var featureFlags = new List<Response<ConfigurationSetting>>
            {
                new Response<ConfigurationSetting>(mockResponse.Object, _kv)
            };

            mock.Setup(c => c.GetSettings(new SettingSelector(), It.IsAny<CancellationToken>()))
                .Returns(new List<Response<ConfigurationSetting>>(featureFlags));

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

            _kv.ETag = new ETag(_kv.ETag.ToString() + "f");

            var mockResponse2 = new Mock<Response>();
            featureFlags.Add(new Response<ConfigurationSetting>(mockResponse2.Object, _kv2));
            options.GetRefresher().Refresh();

            Assert.Equal("Browser", config["FeatureManagement:Beta:EnabledFor:0:Name"]);
            Assert.Equal("Chrome", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:0"]);
            Assert.Equal("Edge", config["FeatureManagement:Beta:EnabledFor:0:Parameters:AllowedBrowsers:1"]);
            Assert.Equal("SuperUsers", config["FeatureManagement:MyFeature2:EnabledFor:0:Name"]);
        }

        [Fact]
        public void PreservesDefaultQuery()
        {
            bool performedDefaultQuery = false;
            bool queriedFeatureFlags = false;

            //var handler = new CallbackMessageHandler((req) =>
            //{
            //    performedDefaultQuery = performedDefaultQuery || req.RequestUri.PathAndQuery == "/kv/?key=*&label=%00";

            //    queriedFeatureFlags = queriedFeatureFlags || req.RequestUri.PathAndQuery.Contains(Uri.EscapeDataString(FeatureManagementConstants.FeatureFlagMarker));

            //    HttpResponseMessage response = new HttpResponseMessage();

            //    response.StatusCode = HttpStatusCode.OK;

            //    response.Content = new StringContent(JsonConvert.SerializeObject(new { items = new object[] { } }), Encoding.UTF8, "application/json");

            //    return response;
            //});

            var mockResponse = new Mock<Response>();
            var mock = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());

            var featureFlags = new List<Response<ConfigurationSetting>>
            {
                new Response<ConfigurationSetting>(mockResponse.Object, _kv)
            };

            mock.Setup(c => c.GetSettings(new SettingSelector(), It.IsAny<CancellationToken>()))
                .Callback<Request>((req) => {
                    performedDefaultQuery = performedDefaultQuery || req.UriBuilder.Uri.PathAndQuery == "/kv/?key=*&label=%00";
                    queriedFeatureFlags = queriedFeatureFlags || req.UriBuilder.Uri.PathAndQuery.Contains(Uri.EscapeDataString(FeatureManagementConstants.FeatureFlagMarker));
                })
                .Returns(new List<Response<ConfigurationSetting>>(featureFlags));

            var testClient = mock.Object;

            var builder = new ConfigurationBuilder();

            var options = new AzureAppConfigurationOptions()
            {
                Client = testClient
            };

            options.UseFeatureFlags();

            builder.AddAzureAppConfiguration(options);

            var config = builder.Build();

            Assert.True(performedDefaultQuery);
            Assert.False(queriedFeatureFlags);

            // TODO: better to separate this into a separate function to create new objects?
            //
            // Reset
            performedDefaultQuery = false;
            queriedFeatureFlags = false;

            mockResponse = new Mock<Response>();
            mock = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());

            featureFlags = new List<Response<ConfigurationSetting>>
            {
                new Response<ConfigurationSetting>(mockResponse.Object, _kv)
            };

            mock.Setup(c => c.GetSettings(new SettingSelector(), It.IsAny<CancellationToken>()))
                .Callback<Request>((req) => {
                    performedDefaultQuery = performedDefaultQuery || req.UriBuilder.Uri.PathAndQuery == "/kv/?key=*&label=%00";
                    queriedFeatureFlags = queriedFeatureFlags || req.UriBuilder.Uri.PathAndQuery.Contains(Uri.EscapeDataString(FeatureManagementConstants.FeatureFlagMarker));
                })
                .Returns(new List<Response<ConfigurationSetting>>(featureFlags));

            builder = new ConfigurationBuilder();

            options = new AzureAppConfigurationOptions()
            {
                Client = testClient
            };

            options.UseFeatureFlags(o => o.Label = "myLabel");

            builder.AddAzureAppConfiguration(options);

            config = builder.Build();

            Assert.True(performedDefaultQuery);
            Assert.True(queriedFeatureFlags);
        }
    }
}
