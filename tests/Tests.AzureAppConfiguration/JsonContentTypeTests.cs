// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.JsonConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class JsonContentTypeTests
    {
        List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>();
        [Fact]
        public void JsonContentTypeTests_CompareJsonSettingsBetweenAppConfigAndJsonFile()
        {
            string appconfigFilePath = "./MockTestData/appconfig-settings.json";
            string jsonFilePath = "./MockTestData/jsonconfig-settings.json";
            _kvCollection.Clear();
            _kvCollection = TestHelpers.LoadJsonSettingsFromFile(appconfigFilePath);
            var mockClient = GetMockConfigurationClient();

            var appconfigSettings = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.Client = mockClient.Object)
                .Build()
                .AsEnumerable();

            var jsonSettings = new ConfigurationBuilder()
                .AddJsonFile(jsonFilePath)
                .Build()
                .AsEnumerable();

            Assert.Equal(jsonSettings.Count(), appconfigSettings.Count());
            foreach (KeyValuePair<string, string> jsonSetting in jsonSettings)
            {
                KeyValuePair<string, string> appconfigSetting = appconfigSettings.SingleOrDefault(x => x.Key == jsonSetting.Key);
                Assert.Equal(jsonSetting, appconfigSetting);
            }
        }

        [Fact]
        public void JsonContentTypeTests_LoadInvalidJsonValueAsStringValue()
        {
            _kvCollection.Clear();
            _kvCollection.Add(
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey1",
                    value: "True",
                    contentType: "application/json"));
            _kvCollection.Add(
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey2",
                    value: "[abc,def,ghi]",
                    contentType: "application/json"));
            _kvCollection.Add(
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey3",
                    value: "{\"Name\": Foo}",
                    contentType: "application/json"));

            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.Client = mockClient.Object)
                .Build();

            Assert.Equal("True", config["TestKey1"]);
            Assert.Equal("[abc,def,ghi]", config["TestKey2"]);
            Assert.Equal("{\"Name\": Foo}", config["TestKey3"]);
        }

        [Fact]
        public void JsonContentTypeTests_LoadSettingsWithInvalidJsonContentTypeAsString()
        {
            _kvCollection.Clear();
            _kvCollection.Add(
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey1",
                    value: "true",
                    contentType: "application/notjson"));
            _kvCollection.Add(
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey2",
                    value: "[1,2,3]",
                    contentType: "text/json"));
            _kvCollection.Add(
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey3",
                    value: "{\"Name\": \"Foo\"}",
                    contentType: null));
            _kvCollection.Add(
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey4",
                    value: "99",
                    contentType: ""));
            _kvCollection.Add(
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey5",
                    value: "null",
                    contentType: "application/vnd.json"));

            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.Client = mockClient.Object)
                .Build();

            Assert.Equal("true", config["TestKey1"]);
            Assert.Equal("[1,2,3]", config["TestKey2"]);
            Assert.Equal("{\"Name\": \"Foo\"}", config["TestKey3"]);
            Assert.Equal("99", config["TestKey4"]);
            Assert.Equal("null", config["TestKey5"]);
        }

        [Fact]
        public void JsonContentTypeTests_DontFlattenFeatureFlagAsJsonObject()
        {
            var compactJsonValue = "{\"id\":\"Beta\",\"description\":\"\",\"enabled\":true,\"conditions\":{\"client_filters\":[{\"name\":\"Browser\",\"parameters\":{\"AllowedBrowsers\":[\"Firefox\",\"Safari\"]}}]}}";
            _kvCollection.Clear();
            _kvCollection.Add(
                ConfigurationModelFactory.ConfigurationSetting(
                    key: FeatureManagementConstants.FeatureFlagMarker + "Beta",
                    value: compactJsonValue,
                    contentType: FeatureManagementConstants.ContentType + ";charset=utf-8"));

            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.Client = mockClient.Object)
                .Build();

            Assert.Equal(compactJsonValue, config[FeatureManagementConstants.FeatureFlagMarker + "Beta"]);
        }

        [Fact]
        public void JsonContentTypeTests_FlattenFeatureFlagWhenContentTypeIsNotFeatureManagementContentType()
        {
            var compactJsonValue = "{\"id\":\"Beta\",\"description\":\"\",\"enabled\":true,\"conditions\":{\"client_filters\":[{\"name\":\"Browser\",\"parameters\":{\"AllowedBrowsers\":[\"Firefox\",\"Safari\"]}}]}}";
            _kvCollection.Clear();
            _kvCollection.Add(
                ConfigurationModelFactory.ConfigurationSetting(
                    key: FeatureManagementConstants.FeatureFlagMarker + "Beta",
                    value: compactJsonValue,
                    contentType: "application/json"));

            var mockClient = GetMockConfigurationClient();

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.Client = mockClient.Object)
                .Build();

            Assert.Equal("Beta", config[FeatureManagementConstants.FeatureFlagMarker + "Beta:id"]);
            Assert.Equal("", config[FeatureManagementConstants.FeatureFlagMarker + "Beta:description"]);
            Assert.Equal("True", config[FeatureManagementConstants.FeatureFlagMarker + "Beta:enabled"]);
            Assert.Equal("Browser", config[FeatureManagementConstants.FeatureFlagMarker + "Beta:conditions:client_filters:0:name"]);
            Assert.Equal("Firefox", config[FeatureManagementConstants.FeatureFlagMarker + "Beta:conditions:client_filters:0:parameters:AllowedBrowsers:0"]);
            Assert.Equal("Safari", config[FeatureManagementConstants.FeatureFlagMarker + "Beta:conditions:client_filters:0:parameters:AllowedBrowsers:1"]);
        }

        [Fact]
        public void JsonContentTypeTests_JsonKeyValueAdapterCannotProcessFeatureFlags()
        {
            var compactJsonValue = "{\"id\":\"Beta\",\"description\":\"\",\"enabled\":true,\"conditions\":{\"client_filters\":[{\"name\":\"Browser\",\"parameters\":{\"AllowedBrowsers\":[\"Firefox\",\"Safari\"]}}]}}";
            ConfigurationSetting setting = ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "Beta",
                value: compactJsonValue,
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8");

            var jsonKeyValueAdapter = new JsonKeyValueAdapter();
            Assert.False(jsonKeyValueAdapter.CanProcess(setting));
        }

        [Fact]
        public void JsonContentTypeTests_JsonKeyValueAdapterCannotProcessKeyVaultReferences()
        {
            ConfigurationSetting setting = ConfigurationModelFactory.ConfigurationSetting(
                key: "TK1",
                value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }
                   ",
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8");

            var jsonKeyValueAdapter = new JsonKeyValueAdapter();
            Assert.False(jsonKeyValueAdapter.CanProcess(setting));
        }

        private Mock<ConfigurationClient> GetMockConfigurationClient()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

            Response<ConfigurationSetting> GetTestKey(string k, string l, CancellationToken ct)
            {
                return Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == k), mockResponse.Object);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection));
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            return mockClient;
        }
    }
}
