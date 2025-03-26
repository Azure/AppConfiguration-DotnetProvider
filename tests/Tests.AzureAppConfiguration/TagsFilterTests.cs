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
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class TagsFilterTests
    {
        private List<ConfigurationSetting> _kvCollection;
        private List<ConfigurationSetting> _ffCollection;

        public TagsFilterTests()
        {
            _kvCollection = new List<ConfigurationSetting>
            {
                CreateConfigurationSetting("TestKey1", "label", "TestValue1", "0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63",
                    new Dictionary<string, string> {
                        { "Environment", "Development" },
                        { "App", "TestApp" }
                    }),

                CreateConfigurationSetting("TestKey2", "label", "TestValue2", "31c38369-831f-4bf1-b9ad-79db56c8b989",
                    new Dictionary<string, string> {
                        { "Environment", "Production" },
                        { "App", "TestApp" }
                    }),

                CreateConfigurationSetting("TestKey3", "label", "TestValue3", "bb203f2b-c113-44fc-995d-b933c2143339",
                    new Dictionary<string, string> {
                        { "Environment", "Development" },
                        { "Component", "API" }
                    }),

                CreateConfigurationSetting("TestKey4", "label", "TestValue4", "bb203f2b-c113-44fc-995d-b933c2143340",
                    new Dictionary<string, string> {
                        { "Environment", "Staging" },
                        { "App", "TestApp" },
                        { "Component", "Frontend" }
                    }),

                CreateConfigurationSetting("TestKey5", "label", "TestValue5", "bb203f2b-c113-44fc-995d-b933c2143341",
                    new Dictionary<string, string> {
                        { "Special:Tag", "Value:With:Colons" },
                        { "Tag@With@At", "Value@With@At" }
                    }),

                CreateConfigurationSetting("TestKey6", "label", "TestValue6", "bb203f2b-c113-44fc-995d-b933c2143342",
                    new Dictionary<string, string> {
                        { "Tag,With,Commas", "Value,With,Commas" },
                        { "Simple", "Tag" },
                        { "EmptyTag", "" },
                        { "NullTag", null }
                    }),

                CreateFeatureFlagSetting("Feature1", "label", true, "0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63",
                    new Dictionary<string, string> {
                        { "Environment", "Development" },
                        { "App", "TestApp" }
                    }),

                CreateFeatureFlagSetting("Feature2", "label", false, "31c38369-831f-4bf1-b9ad-79db56c8b989",
                    new Dictionary<string, string> {
                        { "Environment", "Production" },
                        { "App", "TestApp" }
                    }),

                CreateFeatureFlagSetting("Feature3", "label", true, "bb203f2b-c113-44fc-995d-b933c2143339",
                    new Dictionary<string, string> {
                        { "Environment", "Development" },
                        { "Component", "API" }
                    }),

                CreateFeatureFlagSetting("Feature4", "label", false, "bb203f2b-c113-44fc-995d-b933c2143340",
                    new Dictionary<string, string> {
                        { "Environment", "Staging" },
                        { "App", "TestApp" },
                        { "Component", "Frontend" }
                    }),

                CreateFeatureFlagSetting("Feature5", "label", true, "bb203f2b-c113-44fc-995d-b933c2143341",
                    new Dictionary<string, string> {
                        { "Special:Tag", "Value:With:Colons" },
                        { "Tag@With@At", "Value@With@At" }
                    }),

                CreateFeatureFlagSetting("Feature6", "label", false, "bb203f2b-c113-44fc-995d-b933c2143342",
                    new Dictionary<string, string> {
                        { "Tag,With,Commas", "Value,With,Commas" },
                        { "Simple", "Tag" },
                        { "EmptyTag", "" },
                        { "NullTag", null }
                    }),
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

        private ConfigurationSetting CreateFeatureFlagSetting(string featureId, string label, bool enabled, string etag, IDictionary<string, string> tags)
        {
            string jsonValue = $@"
            {{
                ""id"": ""{featureId}"",
                ""description"": ""Test feature flag"",
                ""enabled"": {enabled.ToString().ToLowerInvariant()},
                ""conditions"": {{
                    ""client_filters"": []
                }}
            }}";

            // Create the feature flag setting
            var setting = ConfigurationModelFactory.ConfigurationSetting(
                key: FeatureManagementConstants.FeatureFlagMarker + featureId,
                label: label,
                value: jsonValue,
                eTag: new ETag(etag),
                contentType: FeatureManagementConstants.ContentType + ";charset=utf-8");

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
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(KeyFilter.Any, "label", new List<string> { "Environment=Development" });
                    });
                })
                .Build();

            // Only TestKey1 and TestKey3 have Environment=Development tag
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);
            Assert.Null(config["TestKey6"]);

            Assert.NotNull(config["FeatureManagement:Feature1"]);
            Assert.NotNull(config["FeatureManagement:Feature3"]);
            Assert.Null(config["FeatureManagement:Feature2"]);
            Assert.Null(config["FeatureManagement:Feature4"]);
            Assert.Null(config["FeatureManagement:Feature5"]);
            Assert.Null(config["FeatureManagement:Feature6"]);
        }

        [Fact]
        public void TagsFilterTests_NullOrEmptyValue()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                s.TagsFilter.Contains("EmptyTag=") &&
                s.TagsFilter.Contains($"NullTag={TagValue.Null}")),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.FindAll(kv =>
                    kv.Tags.ContainsKey("EmptyTag") && kv.Tags["EmptyTag"] == "" &&
                    kv.Tags.ContainsKey("NullTag") && kv.Tags["NullTag"] == null)));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select(KeyFilter.Any, "label", new List<string> { "EmptyTag=", $"NullTag={TagValue.Null}" });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(KeyFilter.Any, "label", new List<string> { "EmptyTag=", $"NullTag={TagValue.Null}" });
                    });
                })
                .Build();

            // Only TestKey6 and Feature6 have EmptyTag and NullTag
            Assert.Null(config["TestKey1"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey3"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);
            Assert.Equal("TestValue6", config["TestKey6"]);

            Assert.Null(config["FeatureManagement:Feature1"]);
            Assert.Null(config["FeatureManagement:Feature2"]);
            Assert.Null(config["FeatureManagement:Feature3"]);
            Assert.Null(config["FeatureManagement:Feature4"]);
            Assert.Null(config["FeatureManagement:Feature5"]);
            Assert.NotNull(config["FeatureManagement:Feature6"]);
        }

        [Fact]
        public void TagsFilterTests_MultipleTagsFiltering()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                s.TagsFilter.Contains("App=TestApp") &&
                s.TagsFilter.Contains("Environment=Development")),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.FindAll(kv =>
                    kv.Tags.ContainsKey("App") && kv.Tags["App"] == "TestApp" &&
                    kv.Tags.ContainsKey("Environment") && kv.Tags["Environment"] == "Development")));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select(KeyFilter.Any, "label", new List<string> { "App=TestApp", "Environment=Development" });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(KeyFilter.Any, "label", new List<string> { "App=TestApp", "Environment=Development" });
                    });
                })
                .Build();

            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey3"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);
            Assert.Null(config["TestKey6"]);

            Assert.NotNull(config["FeatureManagement:Feature1"]);
            Assert.Null(config["FeatureManagement:Feature2"]);
            Assert.Null(config["FeatureManagement:Feature3"]);
            Assert.Null(config["FeatureManagement:Feature4"]);
            Assert.Null(config["FeatureManagement:Feature5"]);
            Assert.Null(config["FeatureManagement:Feature6"]);
        }

        [Fact]
        public void TagsFilterTests_InvalidTagFormat()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            List<string> invalidTagsFilters = new List<string> { "InvalidTagFormat", "=tagValue", "", null };

            foreach (string tagsFilter in invalidTagsFilters)
            {
                // Verify that an ArgumentException is thrown when using an invalid tag format
                var exception = Assert.Throws<ArgumentException>(() =>
                {
                    new ConfigurationBuilder()
                        .AddAzureAppConfiguration(options =>
                        {
                            options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                            options.Select(KeyFilter.Any, "label", new List<string> { tagsFilter });
                        })
                    .Build();
                });

                Assert.Contains($"Tag '{tagsFilter}' does not follow the format \"tagName=tagValue\".", exception.Message);
            }
        }

        [Fact]
        public void TagsFilterTests_TooManyTags()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            List<string> longTagsFilter = new List<string> { "T1=1", "T2=V2", "T3=V3", "T4=V4", "T5=V5", "T6=V6" };

            // Verify that an ArgumentException is thrown when passing more than the allowed number of tags
            var exception = Assert.Throws<ArgumentException>(() =>
            {
                new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                        options.Select(KeyFilter.Any, "label", longTagsFilter);
                    })
                .Build();
            });

            Assert.Contains($"Cannot provide more than 5 tag filters.", exception.Message);
        }

        [Fact]
        public void TagsFilterTests_TagFilterInteractionWithKeyLabelFilters()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            // Setup mock to verify that all three filters (key, label, tags) are correctly applied together
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                (s.KeyFilter == "TestKey*" || s.KeyFilter == FeatureManagementConstants.FeatureFlagMarker + "Feature1") &&
                s.LabelFilter == "label" &&
                s.TagsFilter.Contains("Environment=Development")),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.FindAll(kv =>
                    (kv.Key.StartsWith("TestKey") || kv.Key.StartsWith(FeatureManagementConstants.FeatureFlagMarker + "Feature1")) &&
                    kv.Label == "label" &&
                    kv.Tags.ContainsKey("Environment") &&
                    kv.Tags["Environment"] == "Development")));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select("TestKey*", "label", new List<string> { "Environment=Development" });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select("Feature1", "label", new List<string> { "Environment=Development" });
                    });
                })
                .Build();

            // Only TestKey1 and TestKey3 match all criteria
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);
            Assert.Null(config["TestKey6"]);

            Assert.NotNull(config["FeatureManagement:Feature1"]);
            Assert.Null(config["FeatureManagement:Feature2"]);
            Assert.Null(config["FeatureManagement:Feature3"]);
            Assert.Null(config["FeatureManagement:Feature4"]);
            Assert.Null(config["FeatureManagement:Feature5"]);
            Assert.Null(config["FeatureManagement:Feature6"]);
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
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(KeyFilter.Any, "label", new List<string>());
                    });
                })
                .Build();

            // All keys should be returned when no tag filtering is applied
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue2", config["TestKey2"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Equal("TestValue4", config["TestKey4"]);
            Assert.Equal("TestValue5", config["TestKey5"]);
            Assert.Equal("TestValue6", config["TestKey6"]);

            Assert.NotNull(config["FeatureManagement:Feature1"]);
            Assert.NotNull(config["FeatureManagement:Feature2"]);
            Assert.NotNull(config["FeatureManagement:Feature3"]);
            Assert.NotNull(config["FeatureManagement:Feature4"]);
            Assert.NotNull(config["FeatureManagement:Feature5"]);
            Assert.NotNull(config["FeatureManagement:Feature6"]);
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
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(KeyFilter.Any, "label", new List<string> { "Special:Tag=Value:With:Colons" });
                    });
                })
                .Build();

            // Only TestKey5 has the special character tag
            Assert.Equal("TestValue5", config["TestKey5"]);
            Assert.Null(config["TestKey1"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey3"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey6"]);

            Assert.NotNull(config["FeatureManagement:Feature5"]);
            Assert.Null(config["FeatureManagement:Feature1"]);
            Assert.Null(config["FeatureManagement:Feature2"]);
            Assert.Null(config["FeatureManagement:Feature3"]);
            Assert.Null(config["FeatureManagement:Feature4"]);
            Assert.Null(config["FeatureManagement:Feature6"]);
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
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(KeyFilter.Any, "label", new List<string> { @"Tag\,With\,Commas=Value\,With\,Commas" });
                    });
                })
                .Build();

            // Only TestKey6 has the tag with commas
            Assert.Equal("TestValue6", config["TestKey6"]);
            Assert.Null(config["TestKey1"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey3"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);

            Assert.NotNull(config["FeatureManagement:Feature6"]);
            Assert.Null(config["FeatureManagement:Feature1"]);
            Assert.Null(config["FeatureManagement:Feature2"]);
            Assert.Null(config["FeatureManagement:Feature3"]);
            Assert.Null(config["FeatureManagement:Feature4"]);
            Assert.Null(config["FeatureManagement:Feature5"]);
        }

        [Fact]
        public async Task TagsFilterTests_BasicRefresh()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            IConfigurationRefresher refresher = null;

            var mockAsyncPageable = new MockAsyncPageable(_kvCollection);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Callback(() => mockAsyncPageable.UpdateCollection(_kvCollection.FindAll(kv =>
                    kv.Tags.ContainsKey("Environment") && kv.Tags["Environment"] == "Development")))
                .Returns(mockAsyncPageable);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select(KeyFilter.Any, "label", new List<string> { "Environment=Development" });
                    options.ConfigurationSettingPageIterator = new MockConfigurationSettingPageIterator();
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.RegisterAll();
                        refreshOptions.SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(KeyFilter.Any, "label", new List<string> { "Environment=Development" });
                        ff.SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            // Only TestKey1 and TestKey3 have Environment=Development tag
            Assert.Equal("TestValue1", config["TestKey1"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);
            Assert.Null(config["TestKey6"]);

            Assert.Equal("True", config["FeatureManagement:Feature1"]);
            Assert.NotNull(config["FeatureManagement:Feature3"]);
            Assert.Null(config["FeatureManagement:Feature2"]);
            Assert.Null(config["FeatureManagement:Feature4"]);
            Assert.Null(config["FeatureManagement:Feature5"]);
            Assert.Null(config["FeatureManagement:Feature6"]);

            _kvCollection.Find(setting => setting.Key == "TestKey1").Value = "UpdatedValue1";

            _kvCollection.Find(setting => setting.Key == FeatureManagementConstants.FeatureFlagMarker + "Feature1").Value = $@"
            {{
                ""id"": ""Feature1"",
                ""description"": ""Test feature flag"",
                ""enabled"": false,
                ""conditions"": {{
                    ""client_filters"": []
                }}
            }}";

            await Task.Delay(1500);

            await refresher.RefreshAsync();

            Assert.Equal("UpdatedValue1", config["TestKey1"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);
            Assert.Null(config["TestKey6"]);

            Assert.Equal("False", config["FeatureManagement:Feature1"]);
            Assert.NotNull(config["FeatureManagement:Feature3"]);
            Assert.Null(config["FeatureManagement:Feature2"]);
            Assert.Null(config["FeatureManagement:Feature4"]);
            Assert.Null(config["FeatureManagement:Feature5"]);
            Assert.Null(config["FeatureManagement:Feature6"]);

            _kvCollection.Find(setting => setting.Key == FeatureManagementConstants.FeatureFlagMarker + "Feature1").Value = $@"
            {{
                ""id"": ""Feature1"",
                ""description"": ""Test feature flag"",
                ""enabled"": true,
                ""conditions"": {{
                    ""client_filters"": []
                }}
            }}";

            await Task.Delay(1500);

            await refresher.RefreshAsync();

            Assert.Equal("UpdatedValue1", config["TestKey1"]);
            Assert.Equal("TestValue3", config["TestKey3"]);
            Assert.Null(config["TestKey2"]);
            Assert.Null(config["TestKey4"]);
            Assert.Null(config["TestKey5"]);
            Assert.Null(config["TestKey6"]);

            Assert.Equal("True", config["FeatureManagement:Feature1"]);
            Assert.NotNull(config["FeatureManagement:Feature3"]);
            Assert.Null(config["FeatureManagement:Feature2"]);
            Assert.Null(config["FeatureManagement:Feature4"]);
            Assert.Null(config["FeatureManagement:Feature5"]);
            Assert.Null(config["FeatureManagement:Feature6"]);
        }
    }
}
