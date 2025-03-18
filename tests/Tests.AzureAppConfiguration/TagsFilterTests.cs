// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class TagsFilterTests
    {
        private List<ConfigurationSetting> _kvCollection;

        public TagsFilterTests()
        {
            _kvCollection = new List<ConfigurationSetting>
            {
                CreateConfigurationSetting("TestKey1", "label", "TestValue1", "0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63",
                    new Dictionary<string, string> { { "Environment", "Development" }, { "App", "TestApp" } }),

                CreateConfigurationSetting("TestKey2", "label", "TestValue2", "31c38369-831f-4bf1-b9ad-79db56c8b989",
                    new Dictionary<string, string> { { "Environment", "Production" }, { "App", "TestApp" } }),

                CreateConfigurationSetting("TestKey3", "label", "TestValue3", "bb203f2b-c113-44fc-995d-b933c2143339",
                    new Dictionary<string, string> { { "Environment", "Development" }, { "Component", "API" } }),

                CreateConfigurationSetting("TestKey4", "label", "TestValue4", "bb203f2b-c113-44fc-995d-b933c2143340",
                    new Dictionary<string, string> { { "Environment", "Staging" }, { "App", "TestApp" }, { "Component", "Frontend" } }),

                CreateConfigurationSetting("TestKey5", "label", "TestValue5", "bb203f2b-c113-44fc-995d-b933c2143341",
                    new Dictionary<string, string> { { "Special:Tag", "Value:With:Colons" }, { "Tag@With@At", "Value@With@At" } }),

                CreateConfigurationSetting("TestKey6", "label", "TestValue6", "bb203f2b-c113-44fc-995d-b933c2143342",
                    new Dictionary<string, string> { { "Tag,With,Commas", "Value,With,Commas" }, { "Simple", "Tag" } })
            };
        }

        private ConfigurationSetting CreateConfigurationSetting(string key, string label, string value, string etag, IDictionary<string, string> tags)
        {
            // Create the setting without tags
            var setting = ConfigurationModelFactory.ConfigurationSetting(
                key: key,
                label: label,
                value: value,
                eTag: new ETag(etag),
                contentType: "text");

            // Add tags to the setting
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    setting.Tags.Add(tag.Key, tag.Value);
                }
            }

            return setting;
        }

        [Fact]
        public void TagsFilterTests_BasicTagFiltering()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                s.TagsFilter.Contains("Environment=Development")),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.FindAll(kv =>
                    kv.Tags.ContainsKey("Environment") && kv.Tags["Environment"] == "Development")));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select(KeyFilter.Any, "label", new List<string> { "Environment=Development" });
                })
                .Build();

            // Only TestKey1 and TestKey3 have Environment=Development tag
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);
            Assert.Null(config["TestKey6"]);
        }

        [Fact]
        public void TagsFilterTests_MultipleTagsFiltering()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                s.TagsFilter.Contains("App=TestApp") &&
                s.TagsFilter.Contains("Environment=")),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.FindAll(kv =>
                    kv.Tags.ContainsKey("App") && kv.Tags["App"] == "TestApp" &&
                    kv.Tags.ContainsKey("Environment"))));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select(KeyFilter.Any, "label", new List<string> { "App=TestApp", "Environment=" });
                })
                .Build();

            // TestKey1, TestKey2, and TestKey4 have App=TestApp tag and have Environment tag
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue4", config["TestKey4"]);
            Assert.Null(config["TestKey3"]);  // Has Environment tag but not App=TestApp
            Assert.Null(config["TestKey5"]);
            Assert.Null(config["TestKey6"]);
        }

        [Fact]
        public void TagsFilterTests_InvalidTagFormat()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Verify that an ArgumentException is thrown when using an invalid tag format
            var exception = Assert.Throws<ArgumentException>(() =>
            {
                new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                        options.Select(KeyFilter.Any, "label", new List<string> { "InvalidTagFormat" });
                    })
                .Build();
            });

            Assert.Contains($"Tag 'InvalidTagFormat' does not follow the format \"tag=value\".", exception.Message);
        }

        [Fact]
        public void TagsFilterTests_TagFilterInteractionWithKeyLabelFilters()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Setup mock to verify that all three filters (key, label, tags) are correctly applied together
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                s.KeyFilter == "TestKey*" &&
                s.LabelFilter == "label" &&
                s.TagsFilter.Contains("Environment=Development")),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.FindAll(kv =>
                    kv.Key.StartsWith("TestKey") &&
                    kv.Label == "label" &&
                    kv.Tags.ContainsKey("Environment") &&
                    kv.Tags["Environment"] == "Development")));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select("TestKey*", "label", new List<string> { "Environment=Development" });
                })
                .Build();

            // Only TestKey1 and TestKey3 match all criteria
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);
            Assert.Null(config["TestKey6"]);
        }

        [Fact]
        public void TagsFilterTests_EmptyTagsCollection()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Setup mock to verify behavior with empty tags collection
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                s.TagsFilter.Count == 0),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select(KeyFilter.Any, "label", new List<string>());
                })
                .Build();

            // All keys should be returned when no tag filtering is applied
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Equal("TestValue4", config["TestKey4"]);
            Assert.Equal("TestValue5", config["TestKey5"]);
        }

        [Fact]
        public void TagsFilterTests_SpecialCharactersInTags()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Setup mock for special characters in tags
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                s.TagsFilter.Contains("Special:Tag=Value:With:Colons")),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.FindAll(kv =>
                    kv.Tags.ContainsKey("Special:Tag") && kv.Tags["Special:Tag"] == "Value:With:Colons")));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select(KeyFilter.Any, "label", new List<string> { "Special:Tag=Value:With:Colons" });
                })
                .Build();

            // Only TestKey5 has the special character tag
            Assert.Equal("TestValue5", config["TestKey5"]);
            Assert.Null(config["TestKey1"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey3"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey6"]);
        }

        [Fact]
        public void TagsFilterTests_EscapedCommaCharactersInTags()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Setup mock for comma characters in tags that need to be escaped with backslash
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                s.TagsFilter.Contains(@"Tag\,With\,Commas=Value\,With\,Commas")),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.FindAll(kv =>
                    kv.Tags.ContainsKey("Tag,With,Commas") && kv.Tags["Tag,With,Commas"] == "Value,With,Commas")));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select(KeyFilter.Any, "label", new List<string> { @"Tag\,With\,Commas=Value\,With\,Commas" });
                })
                .Build();

            // Only TestKey6 has the tag with commas
            Assert.Equal("TestValue6", config["TestKey6"]);
            Assert.Null(config["TestKey1"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey3"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);
        }
    }
}
