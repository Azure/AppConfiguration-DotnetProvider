// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
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
        [Fact]
        public void JsonContentTypeTests_CompareJsonSettingsBetweenAppConfigAndJsonFile()
        {
            string appconfigFilePath = "./MockTestData/appconfig-settings.json";
            string jsonFilePath = "./MockTestData/jsonconfig-settings.json";
            List<ConfigurationSetting> _kvCollection = TestHelpers.LoadJsonSettingsFromFile(appconfigFilePath);
            var mockClientManager = GetMockConfigurationClientManager(_kvCollection);

            var appconfigSettings = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.ClientManager = mockClientManager)
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
            List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
            {
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey1",
                    value: "True",
                    contentType: "application/json"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey2",
                    value: "[abc,def,ghi]",
                    contentType: "application/json"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey3",
                    value: "{\"Name\": Foo}",
                    contentType: "APPLICATION/JSON"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey4",
                    value: null,
                    contentType: "APPLICATION/JSON")
            };
            var mockClientManager = GetMockConfigurationClientManager(_kvCollection);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.ClientManager = mockClientManager)
                .Build();

            Assert.Equal("True", config["TestKey1"]);
            Assert.Equal("[abc,def,ghi]", config["TestKey2"]);
            Assert.Equal("{\"Name\": Foo}", config["TestKey3"]);
            Assert.Null(config["TestKey4"]);
        }

        [Fact]
        public void JsonContentTypeTests_LoadSettingsWithInvalidJsonContentTypeAsString()
        {
            List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
            {
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey1",
                    value: "true",
                    contentType: "application/notjson"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey2",
                    value: "[1,2,3]",
                    contentType: "text/json"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey3",
                    value: "{\"Name\": \"Foo\"}",
                    contentType: null),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey4",
                    value: "99",
                    contentType: ""),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey5",
                    value: "null",
                    contentType: "json"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey6",
                    value: null,
                    contentType: "/"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "TestKey7",
                    value: "{}",
                    contentType: "application/")
                };

            var mockClientManager = GetMockConfigurationClientManager(_kvCollection);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.ClientManager = mockClientManager)
                .Build();

            Assert.Equal("true", config["TestKey1"]);
            Assert.Equal("[1,2,3]", config["TestKey2"]);
            Assert.Equal("{\"Name\": \"Foo\"}", config["TestKey3"]);
            Assert.Equal("99", config["TestKey4"]);
            Assert.Equal("null", config["TestKey5"]);
            Assert.Null(config["TestKey6"]);
            Assert.Equal("{}", config["TestKey7"]);
        }

        [Fact]
        public void JsonContentTypeTests_OverwriteValuesForDuplicateKeysAfterFlatteningJson()
        {
            List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
            {
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "MyNumberList",
                    value:  "[10, 20, 30, 40]",
                    contentType: "application/json"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "MyNumberList:0",
                    value: "11",
                    contentType: "application/json"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "MyNumberList:1",
                    value: "22",
                    contentType: "application/json"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "MyNumberList:2",
                    value: "33",
                    contentType: "application/json"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "MyNumberList:3",
                    value: "44",
                    contentType: "application/json"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "MyObject",
                    value: "{\"ObjectSetting\": {\"Logging\": {\"LogLevel\": \"Information\", \"Default\": \"Debug\"}}}",
                    contentType: "application/json"),
                ConfigurationModelFactory.ConfigurationSetting(
                    key: "MyObject:ObjectSetting:Logging:Default",
                    value: "\"Debug2\"",
                    contentType: "application/json"),
                 ConfigurationModelFactory.ConfigurationSetting(
                    key: "MyObject:ObjectSetting:Logging:LogLevel",
                    value: "\"Information2\"",
                    contentType: "application/json"),
                 ConfigurationModelFactory.ConfigurationSetting(
                    key: "MyObjectWithDuplicateProperties",
                    value: "{\"Name\": \"Value1\", \"Name\": \"Value2\"}",
                    contentType: "application/json"),
                 ConfigurationModelFactory.ConfigurationSetting(
                    key: "CaseSensitiveKey",
                    value: "\"foobar\"",
                    contentType: "application/json"),
                 ConfigurationModelFactory.ConfigurationSetting(
                    key: "casesensitivekey",
                    value: "\"foobar-overwritten\"",
                    contentType: "application/json")
                };

            var mockClientManager = GetMockConfigurationClientManager(_kvCollection);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.ClientManager = mockClientManager)
                .Build();

            Assert.Null(config["MyNumberList"]);
            Assert.Equal("11", config["MyNumberList:0"]);
            Assert.Equal("22", config["MyNumberList:1"]);
            Assert.Equal("33", config["MyNumberList:2"]);
            Assert.Equal("44", config["MyNumberList:3"]);

            Assert.Null(config["MyObject"]);
            Assert.Equal("Debug2", config["MyObject:ObjectSetting:Logging:Default"]);
            Assert.Equal("Information2", config["MyObject:ObjectSetting:Logging:LogLevel"]);

            Assert.Null(config["MyObjectWithDuplicateProperties"]);
            Assert.Equal("Value2", config["MyObjectWithDuplicateProperties:Name"]);

            Assert.Equal("foobar-overwritten", config["CaseSensitiveKey"]);
            Assert.Equal("foobar-overwritten", config["casesensitivekey"]);
        }

        [Fact]
        public void JsonContentTypeTests_DontFlattenFeatureFlagAsJsonObject()
        {
            var compactJsonValue = "{\"id\":\"Beta\",\"description\":\"\",\"enabled\":true,\"conditions\":{\"client_filters\":[{\"name\":\"Browser\",\"parameters\":{\"AllowedBrowsers\":[\"Firefox\",\"Safari\"]}}]}}";
            List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
            {
                ConfigurationModelFactory.ConfigurationSetting(
                    key: FeatureManagementConstants.FeatureFlagMarker + "Beta",
                    value: compactJsonValue,
                    contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8")
            };

            var mockClientManager = GetMockConfigurationClientManager(_kvCollection);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.ClientManager = mockClientManager)
                .Build();

            Assert.Equal(compactJsonValue, config[FeatureManagementConstants.FeatureFlagMarker + "Beta"]);
        }

        [Fact]
        public void JsonContentTypeTests_FlattenFeatureFlagWhenContentTypeIsNotFeatureManagementContentType()
        {
            var compactJsonValue = "{\"id\":\"Beta\",\"description\":\"\",\"enabled\":true,\"conditions\":{\"client_filters\":[{\"name\":\"Browser\",\"parameters\":{\"AllowedBrowsers\":[\"Firefox\",\"Safari\"]}}]}}";
            List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
            {
                ConfigurationModelFactory.ConfigurationSetting(
                    key: FeatureManagementConstants.FeatureFlagMarker + "Beta",
                    value: compactJsonValue,
                    contentType: "application/json")
            };

            var mockClientManager = GetMockConfigurationClientManager(_kvCollection);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.ClientManager = mockClientManager)
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
                contentType: FeatureManagementConstants.FeatureFlagContentType + ";charset=utf-8");

            var jsonKeyValueAdapter = new JsonKeyValueAdapter();
            Assert.False(jsonKeyValueAdapter.CanProcess(setting));
        }

        [Fact]
        public void JsonContentTypeTests_JsonKeyValueAdapterCannotProcessDynamicFeatures()
        {
            var compactJsonValue = "{\"id\":\"ShoppingCart\",\"description\":\"\",\"client_assigner\":\"Microsoft.Targeting\",\"variants\":[{\"default\":true,\"name\":\"Big\",\"configuration_reference\":\"ShoppingCart:Big\",\"assignment_parameters\":{\"Audience\":{\"Users\":[\"Alec\"],\"Groups\":[]}}},{\"name\":\"Small\",\"configuration_reference\":\"ShoppingCart:Small\",\"assignment_parameters\":{\"Audience\":{\"Users\":[],\"Groups\":[{\"Name\":\"Ring1\",\"RolloutPercentage\":50}],\"DefaultRolloutPercentage\":30}}}]}";
            ConfigurationSetting setting = ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + "ShoppingCart",
                value: compactJsonValue,
                contentType: FeatureManagementConstants.DynamicFeatureContentType + ";charset=utf-8");

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

        private IConfigurationClientManager GetMockConfigurationClientManager(List<ConfigurationSetting> _kvCollection)
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetTestKey(string k, string l, CancellationToken ct)
            {
                return Response.FromValue(_kvCollection.FirstOrDefault(s => s.Key == k && s.Label == l), mockResponse.Object);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection));
            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            return TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
        }
    }
}
