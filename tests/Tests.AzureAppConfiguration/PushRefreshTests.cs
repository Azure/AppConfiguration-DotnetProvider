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
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
				contentType: "text")
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

		Dictionary<string, EventGridEvent> _eventGridEvents = new Dictionary<string, EventGridEvent>
		{
            {
                "sn;Vxujfidne",
            new EventGridEvent(
                "https://store1.resource.io/kv/searchQuery1",
				"Microsoft.AppConfiguration.KeyValueModified", "2",
                BinaryData.FromString("{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"sn;Vxujfidne\"}")
                )
            },

            {
                "sn;AxRty78B",
            new EventGridEvent(
                "https://store2.resource.io/kv/searchQuery1",
                "Microsoft.AppConfiguration.KeyValueDeleted", "2",
                BinaryData.FromString("{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"sn;AxRty78B\"}")
                )
            },

            {
                "sn;Ttylmable",
            new EventGridEvent(
                "https://store1.resource.io/kv/searchQuery2",
				"Microsoft.AppConfiguration.KeyValueDeleted", "2",
                BinaryData.FromString("{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"sn;Ttylmable\"}")
                )
            },

            {
                "sn;CRAle3342",
            new EventGridEvent(
                "https://store2.resource.io/kv/searchQuery2",
				"Microsoft.AppConfiguration.KeyValueModified", "2",
                BinaryData.FromString("{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"sn;CRAle3342\"}")
                )
            }
        };

		ConfigurationSetting FirstKeyValue => _kvCollection.First();

        [Fact]
        public void ValidatePushNotificationCreation()
        {
			foreach (KeyValuePair<string, EventGridEvent> eventGridAndSync in _eventGridEvents)
            {
				string syncToken = eventGridAndSync.Key;
				EventGridEvent eventGridEvent = eventGridAndSync.Value; 

				Assert.True(eventGridEvent.TryCreatePushNotification(out PushNotification pushNotification));
                Assert.NotNull(pushNotification);
                Assert.Equal(eventGridEvent.EventType, pushNotification.EventType);
                Assert.Equal(eventGridEvent.Subject, pushNotification.ResourceUri.OriginalString);
                Assert.Equal(syncToken, pushNotification.SyncToken);
            }
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
					options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
					options.Select("*");
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
			var clientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);

			var config = new ConfigurationBuilder()
				.AddAzureAppConfiguration(options =>
				{
					options.ClientProvider = clientProvider;
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
			Assert.Equal(_pushNotificationList.Count, clientProvider.UpdateSyncTokenCalled);
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
					options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
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
