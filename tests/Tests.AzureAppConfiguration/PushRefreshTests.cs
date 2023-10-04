// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Messaging.EventGrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class PushRefreshTests
    {
        static readonly Uri PrimaryResourceUri = new Uri(TestHelpers.PrimaryConfigStoreEndpoint.ToString() + "/kv/searchQuery1");
        static readonly Uri SecondaryResourceUri = new Uri(TestHelpers.SecondaryConfigStoreEndpoint.ToString() + "/kv/searchQuery2");

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

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey3",
                label: "label",
                value: "TestValue3",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKeyWithMultipleLabels",
                label: "label1",
                value: "TestValueForLabel1",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKeyWithMultipleLabels",
                label: "label2",
                value: "TestValueForLabel2",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey6",
                label: LabelFilter.Null,
                value: "TestValue6",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType: "text"),
        };

        List<PushNotification> _pushNotificationList = new List<PushNotification>
            {
              new PushNotification  {
                                    ResourceUri = PrimaryResourceUri,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken1;sn=001",
                                    },
              new PushNotification  {
                                    ResourceUri = PrimaryResourceUri,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken2",
                                    },
              new PushNotification  {
                                    ResourceUri = PrimaryResourceUri,
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = "SyncToken1;sn=001",
                                    },
              new PushNotification  {
                                    ResourceUri = PrimaryResourceUri,
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = "SyncToken2",
                                    },
              new PushNotification  {
                                    ResourceUri = SecondaryResourceUri,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken1",
                                    },
              new PushNotification  {
                                    ResourceUri = SecondaryResourceUri,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken2",
                                    },
              new PushNotification  {
                                    ResourceUri = SecondaryResourceUri,
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = "SyncToken1",
                                    },
              new PushNotification  {
                                    ResourceUri = SecondaryResourceUri,
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = "SyncToken2",
                                    }
            };

        List<KeyValuePushNotification> _keyValuePushNotificationList = new List<KeyValuePushNotification>
            {
              new KeyValuePushNotification  {
                                    ResourceUri = PrimaryResourceUri,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken1;sn=001",
                                    Key = "TestKey6",
                                    Label = LabelFilter.Null
                                    },
              new KeyValuePushNotification  {
                                    ResourceUri = SecondaryResourceUri,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken1",
                                    Key = "TestKey3",
                                    Label = "label"
                                    },
              new KeyValuePushNotification  {
                                    ResourceUri = SecondaryResourceUri,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken1",
                                    Key = "TestKeyWithMultipleLabels",
                                    Label = "label1"
                                    },
            };

        List<PushNotification> _invalidPushNotificationList = new List<PushNotification>
            {
              new PushNotification  {
                                    ResourceUri = null,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken1;sn=001"
                                    },
              new PushNotification  {
                                    ResourceUri = SecondaryResourceUri,
                                    EventType = null,
                                    SyncToken = "SyncToken2"
                                    },
              new PushNotification  {
                                    ResourceUri = PrimaryResourceUri,
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = null
                                    },
              new PushNotification  {
                                    ResourceUri = null,
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = null
                                    },
              new PushNotification  {
                                    ResourceUri = null,
                                    EventType = null,
                                    SyncToken = null
                                    }
            };

        List<KeyValuePushNotification> _invalidKeyValuePushNotificationList = new List<KeyValuePushNotification>
            {
              new KeyValuePushNotification  {
                                    ResourceUri = null,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken1;sn=001",
                                    Key = null,
                                    Label = null
                                    },
              new KeyValuePushNotification  {
                                    ResourceUri = SecondaryResourceUri,
                                    EventType = null,
                                    SyncToken = "SyncToken2",
                                    Key = null,
                                    Label = null
                                    },
              new KeyValuePushNotification  {
                                    ResourceUri = PrimaryResourceUri,
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = null,
                                    Key = null,
                                    Label = null
                                    },
              new KeyValuePushNotification  {
                                    ResourceUri = null,
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = null,
                                    Key = null,
                                    Label = null
                                    },
              new KeyValuePushNotification  {
                                    ResourceUri = null,
                                    EventType = null,
                                    SyncToken = null,
                                    Key = null,
                                    Label = null
                                    },
              new KeyValuePushNotification  {
                                    ResourceUri = PrimaryResourceUri,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken3;sn=003",
                                    Key = "Key1",
                                    Label = null
                                    },
              new KeyValuePushNotification  {
                                    ResourceUri = PrimaryResourceUri,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken4;sn=004",
                                    Key = null,
                                    Label = "Label1"
                                    },
            };

        Dictionary<(string, string, string), EventGridEvent> _eventGridEvents = new Dictionary<(string, string, string), EventGridEvent>
        {
            {
                ("sn;Vxujfidne", "searchQuery1", LabelFilter.Null),
            new EventGridEvent(
                "https://store1.resource.io/kv/searchQuery1",
                "Microsoft.AppConfiguration.KeyValueModified", "2",
                BinaryData.FromString("{\"key\":\"searchQuery1\", \"label\": null, \"etag\":\"etagValue1\",\"syncToken\":\"sn;Vxujfidne\"}")
                )
            },
            {
                ("sn;AxRty78B", "searchQuery1", LabelFilter.Null),
            new EventGridEvent(
                "https://store2.resource.io/kv/searchQuery1",
                "Microsoft.AppConfiguration.KeyValueDeleted", "2",
                BinaryData.FromString("{\"key\":\"searchQuery1\", \"label\": null, \"etag\":\"etagValue1\",\"syncToken\":\"sn;AxRty78B\"}")
                )
            },
            {
                ("sn;Ttylmable", "searchQuery1", LabelFilter.Null),
            new EventGridEvent(
                "https://store1.resource.io/kv/searchQuery2",
                "Microsoft.AppConfiguration.KeyValueDeleted", "2",
                BinaryData.FromString("{\"key\":\"searchQuery1\", \"label\": null, \"etag\":\"etagValue1\",\"syncToken\":\"sn;Ttylmable\"}")
                )
            },
            {
                ("sn;CRAle3342", "searchQuery1", LabelFilter.Null),
            new EventGridEvent(
                "https://store2.resource.io/kv/searchQuery2",
                "Microsoft.AppConfiguration.KeyValueModified", "2",
                BinaryData.FromString("{\"key\":\"searchQuery1\", \"label\": null, \"etag\":\"etagValue1\",\"syncToken\":\"sn;CRAle3342\"}")
                )
            }
        };

        ConfigurationSetting FirstKeyValue => _kvCollection.First();

        [Fact]
        public void ValidatePushNotificationCreation()
        {
            foreach (KeyValuePair<(string, string, string), EventGridEvent> eventGridAndSync in _eventGridEvents)
            {
                string syncToken = eventGridAndSync.Key.Item1;
                EventGridEvent eventGridEvent = eventGridAndSync.Value;

                Assert.True(eventGridEvent.TryCreatePushNotification(out PushNotification pushNotification));
                Assert.NotNull(pushNotification);
                Assert.Equal(eventGridEvent.EventType, pushNotification.EventType);
                Assert.Equal(eventGridEvent.Subject, pushNotification.ResourceUri.OriginalString);
                Assert.Equal(syncToken, pushNotification.SyncToken);
            }
        }

        [Fact]
        public void ValidateKeyValuePushNotificationCreation()
        {
            foreach (KeyValuePair<(string, string, string), EventGridEvent> eventGridAndSync in _eventGridEvents)
            {
                string syncToken = eventGridAndSync.Key.Item1;
                EventGridEvent eventGridEvent = eventGridAndSync.Value;

                Assert.True(eventGridEvent.TryCreateKeyValuePushNotification(out KeyValuePushNotification keyValuePushNotification));
                Assert.NotNull(keyValuePushNotification);
                Assert.Equal(eventGridEvent.EventType, keyValuePushNotification.EventType);
                Assert.Equal(eventGridEvent.Subject, keyValuePushNotification.ResourceUri.OriginalString);
                Assert.Equal(syncToken, keyValuePushNotification.SyncToken);
                Assert.Equal(eventGridAndSync.Key.Item2, keyValuePushNotification.Key);
                Assert.Equal(eventGridAndSync.Key.Item3, keyValuePushNotification.Label);
            }
        }

        [Fact]
        public void ProcessKeyValuePushNotificationThrowsArgumentExceptions()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = GetMockConfigurationClient();

            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select(KeyFilter.Any);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.RegisterUpdatesOnly()
                            .SetCacheExpiration(TimeSpan.FromDays(30));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            foreach (KeyValuePushNotification invalidKeyValuePushNotification in _invalidKeyValuePushNotificationList)
            {
                Action action = () => refresher.ProcessKeyValuePushNotification(invalidKeyValuePushNotification);
                Assert.Throws<ArgumentException>(action);
            }

            KeyValuePushNotification nullPushNotification = null;

            Action nullAction = () => refresher.ProcessKeyValuePushNotification(nullPushNotification);
            Assert.Throws<ArgumentNullException>(nullAction);
        }

        [Fact]
        public void ProcessPushNotificationThrowsArgumentExceptions()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = GetMockConfigurationClient();

            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.Select(KeyFilter.Any);
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromDays(30));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            foreach (PushNotification invalidPushNotification in _invalidPushNotificationList)
            {
                Action action = () => refresher.ProcessPushNotification(invalidPushNotification);
                Assert.Throws<ArgumentException>(action);
            }

            PushNotification nullPushNotification = null;

            Action nullAction = () => refresher.ProcessPushNotification(nullPushNotification);
            Assert.Throws<ArgumentNullException>(nullAction);
        }

        [Fact]
        public void SyncTokenUpdatesCorrectNumberOfTimes()
        {
            // Arrange
            var mockResponse = new Mock<Response>();
            var mockClient = GetMockConfigurationClient();

            IConfigurationRefresher refresher = null;
            var clientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = clientManager;
                    options.Select("*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromDays(30));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            foreach (PushNotification pushNotification in _pushNotificationList)
            {
                refresher.ProcessPushNotification(pushNotification, TimeSpan.FromSeconds(0));
                refresher.RefreshAsync().Wait();
            }

            var validNotificationKVWatcherCount = 8;
            var validEndpointCount = 4;

            mockClient.Verify(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(validNotificationKVWatcherCount));
            Assert.Equal(_pushNotificationList.Count, clientManager.UpdateSyncTokenCalled);
            mockClient.Verify(c => c.UpdateSyncToken(It.IsAny<string>()), Times.Exactly(validEndpointCount));
        }

        [Fact]
        public void RefreshAsyncUpdatesConfig()
        {
            // Arrange
            var mockResponse = new Mock<Response>();
            var mockClient = GetMockConfigurationClient();

            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object); ;
                    options.Select("*");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("TestKey1", "label")
                            .SetCacheExpiration(TimeSpan.FromDays(30));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();


            Assert.Equal("TestValue1", config["TestKey1"]);
            FirstKeyValue.Value = "newValue1";

            refresher.ProcessPushNotification(_pushNotificationList.First(), TimeSpan.FromSeconds(0));
            refresher.RefreshAsync().Wait();

            Assert.Equal("newValue1", config["TestKey1"]);
        }

        [Fact]
        public void RefreshUpdatedConfigurationOnly()
        {
            // Arrange
            var mockResponse = new Mock<Response>();
            var mockClient = GetMockConfigurationClient();

            IConfigurationRefresher refresher = null;

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object); ;
                    options.Select(KeyFilter.Any, LabelFilter.Null);
                    options.Select(KeyFilter.Any, "label");
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.RegisterUpdatesOnly()
                            .SetCacheExpiration(TimeSpan.FromDays(30));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("TestValueForLabel2", config["TestKeyWithMultipleLabels"]);
            Assert.Equal("TestValue6", config["TestKey6"]);
            Assert.Equal("TestValue3", config["TestKey3"]);

            // Test a value that is not in our label. Should not be reloaded.
            _kvCollection[3].Value = "TestValueForLabel1NoChange";
            // Third Value
            _kvCollection[2].Value = "newValue3";
            // Sixth value
            _kvCollection[5].Value = "newValue6";
            refresher.ProcessKeyValuePushNotification(_keyValuePushNotificationList[0], TimeSpan.Zero);
            refresher.ProcessKeyValuePushNotification(_keyValuePushNotificationList[1], TimeSpan.Zero);
            refresher.ProcessKeyValuePushNotification(_keyValuePushNotificationList[2], TimeSpan.Zero);
            refresher.RefreshAsync().Wait();

            Assert.Equal("TestValueForLabel2", config["TestKeyWithMultipleLabels"]);
            Assert.Equal("newValue6", config["TestKey6"]);
            Assert.Equal("newValue3", config["TestKey3"]);
        }

        private Mock<ConfigurationClient> GetMockConfigurationClient()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                return Response.FromValue(TestHelpers.CloneSetting(_kvCollection.FirstOrDefault(s => s.Key == key && s.Label == label)), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
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

            mockClient.Setup(c => c.UpdateSyncToken(It.IsAny<string>()));

            return mockClient;
        }
    }

}
