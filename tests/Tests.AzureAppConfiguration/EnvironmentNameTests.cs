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
            var mockClient = GetMockConfigurationClientSelectKeyLabel();
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

            Assert.Equal("Development", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            // Key-values will not be loaded in the wrong environment 
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
        public void AllowLoadingKeyValuesWithAspNetCoreEnvironmentName()
        {
            var mockClient = GetMockConfigurationClientSelectKeyLabel();

            Environment.SetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable, "Production");
            Assert.Equal("Production", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(environmentName: "Production", options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("TestKey1", "label");
                    options.Select("TestKey2", "label");
                })
                .Build();

            Assert.Equal("Production", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
        }

        [Fact]
        public void AllowLoadingKeyValuesWithDotNetCoreEnvironmentName()
        {
            var mockClient = GetMockConfigurationClientSelectKeyLabel();

            Environment.SetEnvironmentVariable(RequestTracingConstants.DotNetCoreEnvironmentVariable, "Production");
            Environment.SetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable, null);
            Assert.Equal("Production", Environment.GetEnvironmentVariable(RequestTracingConstants.DotNetCoreEnvironmentVariable));
            Assert.Null(Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(environmentName: "Production", options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("TestKey1", "label");
                    options.Select("TestKey2", "label");
                })
                .Build();

            Assert.Equal("Production", Environment.GetEnvironmentVariable(RequestTracingConstants.DotNetCoreEnvironmentVariable));
            Assert.Null(Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
        }

        [Fact]
        public void LoadingKeyValuesWithEnvironmentNameInMultipleConfigurations()
        {
            var mockClientDev = GetMockConfigurationClientSelectKeyLabel();
            var mockClientProd = GetMockConfigurationClientSelectKeyLabel();

            Environment.SetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable, "Production");
            Assert.Equal("Production", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(environmentName: "Production", options =>
                {
                    options.Client = mockClientProd.Object;
                    options.Select("TestKey1", "label");
                })
                .AddAzureAppConfiguration(environmentName: "Development", options =>
                {
                    options.Client = mockClientDev.Object;
                    options.Select("TestKey2", "label");
                })
                .Build();

            Assert.Equal("Production", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Null(config["TestKey2"]);
        }

        [Fact]
        public void PreventLoadingKeyValuesWithoutNullEnvironmentName()
        {
            var mockClient = GetMockConfigurationClientSelectKeyLabel();

            Environment.SetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable, "Production");
            Assert.Equal("Production", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(environmentName: null, options =>
                {
                    options.Client = mockClient.Object;
                    options.Select("TestKey1", "label");
                    options.Select("TestKey2", "label");
                })
                .Build();

            Assert.Equal("Production", Environment.GetEnvironmentVariable(RequestTracingConstants.AspNetCoreEnvironmentVariable));

            Assert.Null(config["TestKey1"]);
            Assert.Null(config["TestKey2"]);
        }

        private Mock<ConfigurationClient> GetMockConfigurationClientSelectKeyLabel()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            MockAsyncPageable GetTestKeys(SettingSelector selector, CancellationToken ct)
            {
                var copy = new List<ConfigurationSetting>();
                var newSetting = _kvCollection.FirstOrDefault(s => (s.Key == selector.KeyFilter && s.Label == selector.LabelFilter));
                if (newSetting != null)
                    copy.Add(TestHelpers.CloneSetting(newSetting));
                return new MockAsyncPageable(copy);
            }

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                return Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == key && s.Label == label), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var newSetting = _kvCollection.FirstOrDefault(s => (s.Key == setting.Key && s.Label == setting.Label));
                var unchanged = (newSetting.Key == setting.Key && newSetting.Label == setting.Label && newSetting.Value == setting.Value);
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(newSetting, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns((Func<SettingSelector, CancellationToken, MockAsyncPageable>)GetTestKeys);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            return mockClient;
        }
    }
}
