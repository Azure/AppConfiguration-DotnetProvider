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
            string[] tagFilters = { "Environment=Development" };
            MockTransport mockTransport = CreateRefreshTransport();
            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientOptions.Transport = mockTransport;
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(options);
                    options.Select(KeyFilter.Any, "label", tagFilters);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.RegisterAll();
                        refreshOptions.SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    options.UseFeatureFlags(ff =>
                    {
                        ff.Select(KeyFilter.Any, "label", tagFilters);
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
        [InlineData(false, null)]
        [InlineData(true, null)]
        [InlineData(false, new string[] { })]
        [InlineData(true, new string[] { })]
        [InlineData(false, new[] { "Environment=Development" })]
        [InlineData(true, new[] { "Environment=Development" })]
        [InlineData(false, new[] { "Environment=Missing" })]
        [InlineData(true, new[] { "Environment=Missing" })]
        [InlineData(false, new[] { "Environment=Development", "App=TestApp" })]
        [InlineData(true, new[] { "Environment=Development", "App=TestApp" })]
        [InlineData(false, new[] { @"Tag\,With\,Commas=Value\,With\,Commas" })]
        [InlineData(true, new[] { @"Tag\,With\,Commas=Value\,With\,Commas" })]
        [InlineData(false, new[] { "EmptyTag=", "NullTag=" + TagValue.Null })]
        [InlineData(true, new[] { "EmptyTag=", "NullTag=" + TagValue.Null })]
        public async Task TagFiltersTests_UnchangedCollectionsDoNotReload(bool featureFlags, string[] tagFilters)
        {
            MockTransport transport = CreateRefreshTransport();
            (IConfigurationRoot configuration, IConfigurationRefresher refresher) =
                BuildRefreshConfiguration(transport, featureFlags, tagFilters);
            using IDisposable configurationLifetime = (IDisposable)configuration;

            string loadQuery = transport.Requests.Last().Uri.ToUri().Query;
            int initialRequestCount = transport.Requests.Count;
            KeyValuePair<string, string>[] initialData = configuration.AsEnumerable().OrderBy(setting => setting.Key).ToArray();

            await AssertUnchangedAfterRefreshAsync(configuration, refresher, transport);
            await AssertUnchangedAfterRefreshAsync(configuration, refresher, transport);

            Assert.Equal(initialData, configuration.AsEnumerable().OrderBy(setting => setting.Key));
            Assert.All(transport.Requests.Skip(initialRequestCount),
                request => Assert.Equal(loadQuery, request.Uri.ToUri().Query));
        }

        [Theory]
        [InlineData(false, "Update")]
        [InlineData(true, "Update")]
        [InlineData(false, "Add")]
        [InlineData(true, "Add")]
        [InlineData(false, "Delete")]
        [InlineData(true, "Delete")]
        [InlineData(false, "TagAdded")]
        [InlineData(true, "TagAdded")]
        [InlineData(false, "TagRemoved")]
        [InlineData(true, "TagRemoved")]
        public async Task TagFiltersTests_RefreshDetectsSelectedChanges(bool featureFlags, string change)
        {
            string[] tagFilters = { "Environment=Development", "App=TestApp" };
            string key = featureFlags ? FeatureManagementConstants.FeatureFlagMarker + "Feature1" : "TestKey1";
            string configurationKey = featureFlags ? "FeatureManagement:Feature1" : "TestKey1";
            ConfigurationSetting setting = _kvCollection.Single(kv => kv.Key == key);

            if (change == "Add" || change == "TagAdded")
            {
                setting.Tags.Remove("Environment");
            }

            MockTransport transport = CreateRefreshTransport();
            (IConfigurationRoot configuration, IConfigurationRefresher refresher) =
                BuildRefreshConfiguration(transport, featureFlags, tagFilters);
            using IDisposable configurationLifetime = (IDisposable)configuration;

            IChangeToken reloadToken = configuration.GetReloadToken();
            int initialRequestCount = transport.Requests.Count;
            var expected = new Dictionary<string, string>();

            switch (change)
            {
                case "Update":
                    setting.Value = featureFlags ? setting.Value.Replace("true", "false") : "UpdatedValue1";
                    expected[configurationKey] = featureFlags ? "False" : "UpdatedValue1";
                    break;
                case "Add":
                    var tags = new Dictionary<string, string>
                    {
                        { "Environment", "Development" },
                        { "App", "TestApp" }
                    };
                    _kvCollection.Add(featureFlags
                        ? CreateFeatureFlagSetting("Feature7", "label", true, "new", tags)
                        : CreateConfigurationSetting("TestKey7", "label", "NewValue", "new", tags));
                    expected[featureFlags ? "FeatureManagement:Feature7" : "TestKey7"] = featureFlags ? "True" : "NewValue";
                    break;
                case "Delete":
                    _kvCollection.Remove(setting);
                    break;
                case "TagAdded":
                    setting.Tags["Environment"] = "Development";
                    expected[configurationKey] = featureFlags ? "True" : "TestValue1";
                    break;
                case "TagRemoved":
                    setting.Tags.Remove("Environment");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown change.");
            }

            await Task.Delay(1500);
            await refresher.RefreshAsync();

            Assert.True(reloadToken.HasChanged);
            Assert.Equal(initialRequestCount + 2, transport.Requests.Count);
            Assert.Equal(expected.OrderBy(kv => kv.Key),
                configuration.AsEnumerable().Where(kv => kv.Value != null).OrderBy(kv => kv.Key));

            await AssertUnchangedAfterRefreshAsync(configuration, refresher, transport);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TagFiltersTests_RefreshTracksSelectorsWithDifferentTags(bool featureFlags)
        {
            string[][] tagFilterSets =
            {
                new[] { "Environment=Development" },
                new[] { "Environment=Production" }
            };
            MockTransport transport = CreateRefreshTransport();
            (IConfigurationRoot configuration, IConfigurationRefresher refresher) =
                BuildRefreshConfiguration(transport, featureFlags, tagFilterSets);
            using IDisposable configurationLifetime = (IDisposable)configuration;

            string[] loadQueries = transport.Requests.Skip(featureFlags ? 1 : 0)
                .Select(request => request.Uri.ToUri().Query).ToArray();
            int initialRequestCount = transport.Requests.Count;

            await AssertUnchangedAfterRefreshAsync(configuration, refresher, transport, expectedChecks: 2);

            Assert.Equal(loadQueries, transport.Requests.Skip(initialRequestCount)
                .Select(request => request.Uri.ToUri().Query));

            string key = featureFlags ? FeatureManagementConstants.FeatureFlagMarker + "Feature1" : "TestKey1";
            ConfigurationSetting setting = _kvCollection.Single(kv => kv.Key == key);
            setting.Value = featureFlags ? setting.Value.Replace("true", "false") : "UpdatedValue1";
            IChangeToken reloadToken = configuration.GetReloadToken();
            int requestCount = transport.Requests.Count;

            await Task.Delay(1500);
            await refresher.RefreshAsync();

            Assert.True(reloadToken.HasChanged);
            Assert.Equal(requestCount + 3, transport.Requests.Count);
            Assert.Equal(featureFlags ? "False" : "UpdatedValue1",
                configuration[featureFlags ? "FeatureManagement:Feature1" : "TestKey1"]);

            await AssertUnchangedAfterRefreshAsync(configuration, refresher, transport, expectedChecks: 2);
        }

        [Theory]
        [InlineData("label", "label")]
        [InlineData(null, LabelFilter.Null)]
        public void TagFiltersTests_WatcherIdentityIncludesTags(string label, string equivalentLabel)
        {
            var watcher = new KeyValueWatcher
            {
                Key = "TestKey*",
                Label = label,
                Tags = new[] { "Environment=Development", "App=TestApp" }
            };
            var equivalentWatcher = new KeyValueWatcher
            {
                Key = "TestKey*",
                Label = equivalentLabel,
                Tags = new[] { "App=TestApp", "Environment=Development", "App=TestApp" }
            };
            var differentWatcher = new KeyValueWatcher
            {
                Key = "TestKey*",
                Label = label,
                Tags = new[] { "Environment=Production", "App=TestApp" }
            };

            Assert.Equal(watcher, equivalentWatcher);
            Assert.Equal(watcher.GetHashCode(), equivalentWatcher.GetHashCode());
            Assert.NotEqual(watcher, differentWatcher);
            Assert.Equal(2, new HashSet<KeyValueWatcher> { watcher, equivalentWatcher, differentWatcher }.Count);
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
