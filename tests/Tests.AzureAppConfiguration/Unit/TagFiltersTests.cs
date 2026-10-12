// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Data.AppConfiguration.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using Microsoft.Extensions.Primitives;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class TagFiltersTests
    {
        private List<ConfigurationSetting> _kvCollection;
        private const int MaxTagFilters = 5;

        public TagFiltersTests()
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
        public void TagFiltersTests_BasicTagFiltering()
        {
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
        public void TagFiltersTests_NullOrEmptyValue()
        {
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
        public void TagFiltersTests_MultipleTagsFiltering()
        {
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
        public void TagFiltersTests_InvalidTagFormat()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            List<string> invalidTagFilters = new List<string> { "InvalidTagFormat", "=tagValue", "", null };

            foreach (string tagsFilter in invalidTagFilters)
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

                Assert.Contains($"Tag filter '{tagsFilter}' does not follow the format \"tagName=tagValue\".", exception.Message);
            }
        }

        [Fact]
        public void TagFiltersTests_TooManyTags()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var mockResponse = new Mock<Response>();

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                s.TagsFilter.Contains("Environment=Development") && s.TagsFilter.Count <= MaxTagFilters),
                It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection.FindAll(kv =>
                    kv.Tags.ContainsKey("Environment") && kv.Tags["Environment"] == "Development")));

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.Is<SettingSelector>(s =>
                s.TagsFilter.Count > MaxTagFilters),
                It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException($"Invalid parameter TagsFilter. Maximum filters is {MaxTagFilters}"));

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

            List<string> longTagsFilter = new List<string>
            {
                "Environment=Development",
                "Environment=Development",
                "Environment=Development",
                "Environment=Development",
                "Environment=Development",
                "Environment=Development"
            };

            // Verify that a RequestFailedException is thrown when passing more than the allowed number of tags
            var exception = Assert.Throws<RequestFailedException>(() =>
            {
                new ConfigurationBuilder()
                    .AddAzureAppConfiguration(options =>
                    {
                        options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                        options.Select(KeyFilter.Any, "label", longTagsFilter);
                    })
                .Build();
            });
        }

        [Fact]
        public void TagFiltersTests_TagFilterInteractionWithKeyLabelFilters()
        {
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
        public void TagFiltersTests_EmptyTagsCollection()
        {
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
        public void TagFiltersTests_SpecialCharactersInTags()
        {
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
        public void TagFiltersTests_EscapedCommaCharactersInTags()
        {
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
        public async Task TagFiltersTests_BasicRefresh()
        {
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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TagFiltersTests_WatcherIdentityWithoutTags(bool emptyTags)
        {
            var watcher = new KeyValueWatcher
            {
                Key = "TestKey*",
                Label = "label",
                Tags = emptyTags ? Array.Empty<string>() : null
            };
            var equivalentWatcher = new KeyValueWatcher
            {
                Key = "TestKey*",
                Label = "label",
                Tags = Array.Empty<string>()
            };
            var filteredWatcher = new KeyValueWatcher
            {
                Key = "TestKey*",
                Label = "label",
                Tags = new[] { "Environment=Development" }
            };

            Assert.Equal(watcher, equivalentWatcher);
            Assert.Equal(equivalentWatcher, watcher);
            Assert.Equal(watcher.GetHashCode(), equivalentWatcher.GetHashCode());
            Assert.NotEqual(watcher, filteredWatcher);
            Assert.NotEqual(equivalentWatcher, filteredWatcher);
            Assert.Equal(2, new HashSet<KeyValueWatcher> { watcher, equivalentWatcher, filteredWatcher }.Count);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TagFiltersTests_SelectorIdentityWithoutTags(bool emptyTags)
        {
            var selector = new KeyValueSelector
            {
                KeyFilter = "TestKey*",
                LabelFilter = "label",
                TagFilters = emptyTags ? Array.Empty<string>() : null
            };
            var equivalentSelector = new KeyValueSelector
            {
                KeyFilter = "TestKey*",
                LabelFilter = "label",
                TagFilters = Array.Empty<string>()
            };
            var filteredSelector = new KeyValueSelector
            {
                KeyFilter = "TestKey*",
                LabelFilter = "label",
                TagFilters = new[] { "Environment=Development" }
            };

            Assert.Equal(selector, equivalentSelector);
            Assert.Equal(equivalentSelector, selector);
            Assert.Equal(selector.GetHashCode(), equivalentSelector.GetHashCode());
            Assert.NotEqual(selector, filteredSelector);
            Assert.NotEqual(equivalentSelector, filteredSelector);
            Assert.Equal(2, new HashSet<KeyValueSelector> { selector, equivalentSelector, filteredSelector }.Count);
        }

        private MockTransport CreateRefreshTransport()
        {
            return new MockTransport(request =>
            {
                Uri uri = request.Uri.ToUri();
                Assert.Equal("/kv", uri.AbsolutePath);
                ILookup<string, string> query = uri.Query.TrimStart('?').Split('&')
                    .Select(parameter => parameter.Split(new[] { '=' }, 2))
                    .ToLookup(parameter => Uri.UnescapeDataString(parameter[0]),
                        parameter => Uri.UnescapeDataString(parameter[1]));
                string keyFilter = query["key"].Single();
                string labelFilter = query["label"].Single();
                string[] tagFilters = query["tags"].ToArray();
                Assert.EndsWith("*", keyFilter);
                string keyPrefix = keyFilter.Substring(0, keyFilter.Length - 1);
                IEnumerable<ConfigurationSetting> settings = _kvCollection.Where(setting =>
                    setting.Key.StartsWith(keyPrefix, StringComparison.Ordinal) && setting.Label == labelFilter);
                settings = settings.Where(setting => tagFilters.All(filter => setting.Tags.Any(tag =>
                    $"{EscapeTag(tag.Key)}={EscapeTag(tag.Value)}" == filter)));

                byte[] content = SerializationHelpers.Serialize(settings.ToArray(), TestHelpers.SerializeBatch);
                using SHA256 hash = SHA256.Create();
                string eTag = $"\"{Convert.ToBase64String(hash.ComputeHash(content))}\"";
                bool unchanged = request.Headers.TryGetValue("If-None-Match", out string ifNoneMatch) && ifNoneMatch == eTag;
                var response = new MockResponse(unchanged ? 304 : 200, eTag: eTag);

                if (!unchanged)
                {
                    response.SetContent(content);
                }

                return response;
            });
        }

        private (IConfigurationRoot Configuration, IConfigurationRefresher Refresher) BuildRefreshConfiguration(
            MockTransport transport, bool featureFlags, params string[][] tagFilterSets)
        {
            IConfigurationRefresher refresher = null;
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientOptions.Transport = transport;
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(options);

                    if (featureFlags)
                    {
                        options.Select("Unselected*", "label");
                        options.UseFeatureFlags(flags =>
                        {
                            foreach (string[] tagFilters in tagFilterSets)
                            {
                                flags.Select(KeyFilter.Any, "label", tagFilters);
                            }

                            flags.SetRefreshInterval(TimeSpan.FromSeconds(1));
                        });
                    }
                    else
                    {
                        foreach (string[] tagFilters in tagFilterSets)
                        {
                            options.Select("TestKey*", "label", tagFilters);
                        }

                        options.ConfigureRefresh(refresh => refresh.RegisterAll().SetRefreshInterval(TimeSpan.FromSeconds(1)));
                    }

                    refresher = options.GetRefresher();
                })
                .Build();

            return (configuration, refresher);
        }

        private static async Task AssertUnchangedAfterRefreshAsync(
            IConfigurationRoot configuration, IConfigurationRefresher refresher, MockTransport transport,
            int expectedChecks = 1, CancellationToken cancellationToken = default)
        {
            IChangeToken reloadToken = configuration.GetReloadToken();
            int requestCount = transport.Requests.Count;

            await Task.Delay(1500, cancellationToken);
            await refresher.RefreshAsync(cancellationToken);

            Assert.False(reloadToken.HasChanged);
            Assert.Equal(requestCount + expectedChecks, transport.Requests.Count);
            Assert.All(transport.Requests.Skip(requestCount),
                request => Assert.True(request.Headers.TryGetValue("If-None-Match", out _)));
        }

        private static string EscapeTag(string value)
        {
            return value == null ? TagValue.Null : value.Replace("\\", "\\\\").Replace("*", "\\*").Replace(",", "\\,");
        }
    }
}
