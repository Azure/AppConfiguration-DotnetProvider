using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using Azure.Data.AppConfiguration;
using Azure;
using System.Linq;
using Moq;
using Azure.Core.Testing;

namespace Tests.AzureAppConfiguration
{
    public class EnvironmentNameTests
    {
        List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey1",
                label: "label",
                value: "TestValue1",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey2",
                label: "label",
                value: "TestValue2",
                eTag: new ETag("31c38369-831f-4bf1-b9ad-79db56c8b989"),
                contentType: "text"),
        };

        [Fact]
        public void PreventLoadingKeyValuesWithEnvironmentNameAfterStartup()
        {
            var mockClient = GetMockConfigurationClient();
            IConfigurationRefresher refresher = null;
            TimeSpan cacheExpirationTime = TimeSpan.FromSeconds(2);

            Environment.SetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable, "Development");
            Assert.Equal("Development", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(environmentName: "Staging", options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label").Register("TestKey2", "label").SetCacheExpiration(cacheExpirationTime);
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            // Key-values will not be loaded in the wrong environment 
            Assert.Equal("Development", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            Assert.Null(config["TestKey1"]);
            Assert.Null(config["TestKey2"]);

            Environment.SetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable, "Staging");

            Thread.Sleep(cacheExpirationTime);
            refresher.TryRefreshAsync();

            Assert.Equal("Staging", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            // Regardless of changes to environment variables at runtime, the behavior established at startup will be used
            Assert.Null(config["TestKey1"]);
            Assert.Null(config["TestKey2"]);
        }

        [Fact]
        public void AllowReadingKeyValuesWithEnvironmentName()
        {
            var mockClient = GetMockConfigurationClient();

            Environment.SetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable, "Production");
            Assert.Equal("Production", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(environmentName: "Production", options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label").Register("TestKey2", "label");
                    });
                })
                .Build();

            // Key-values will not be read in the wrong environment 
            Assert.Equal("Production", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
        }

        private Mock<ConfigurationClient> GetMockConfigurationClient()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return Response.FromValue(TestHelpers.CloneSetting(_kvCollection.FirstOrDefault(s => s.Key == key && s.Label == label)), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var newSetting = _kvCollection.FirstOrDefault(s => (s.Key == setting.Key && s.Label == setting.Label));
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            // We don't actually select KV based on SettingSelector, we just return a deep copy of _kvCollection
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    return new MockAsyncPageable(_kvCollection.Select(setting => TestHelpers.CloneSetting(setting)).ToList());
                });

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            return mockClient;
        }
    }
}
