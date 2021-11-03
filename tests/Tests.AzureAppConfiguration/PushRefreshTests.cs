using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Azure.EventGrid.Models;
using Xunit;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;


namespace Tests.AzureAppConfiguration
{
	//class EventGridEvent
	//{
	//	public string data { get; set; }
	//	public string dataVersion { get; set; }
	//	public System.DateTimeOffset eventTime { get; set; }
	//	public string eventType { get; set; }
	//	public string id { get; set; }
	//	public Uri subject { get; set; }
	//	public string topic { get; set; }
	//}
	public class PushRefreshTests
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

        List<PushNotification> pushNotificationList = new List<PushNotification>
            {
              new PushNotification  {
                                    ResourceUri = new Uri("https://store1.resource.io/kv/searchQuery1"),
									EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken1;sn=001"
                                    },
              new PushNotification  {
                                    ResourceUri = new Uri("https://store1.resource.io/kv/searchQuery1"),
									EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken2"
                                    },
              new PushNotification  {
                                    ResourceUri = new Uri("https://store1.resource.io/kv/searchQuery1"),
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = "SyncToken1;sn=001"
                                    },
              new PushNotification  {
                                    ResourceUri = new Uri("https://store1.resource.io/kv/searchQuery1"),
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = "SyncToken2"
                                    },
              new PushNotification  {
                                    ResourceUri = new Uri("https://store2.resource.io/kv/searchQuery2"),
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken1"
                                    },
              new PushNotification  {
                                    ResourceUri = new Uri("https://store2.resource.io/kv/searchQuery2"),
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken2"
                                    },
              new PushNotification  {
                                    ResourceUri = new Uri("https://store2.resource.io/kv/searchQuery2"),
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = "SyncToken1"
                                    },
              new PushNotification  {
                                    ResourceUri = new Uri("https://store2.resource.io/kv/searchQuery2"),
                                    EventType = "eventType.KeyValueDeleted",
                                    SyncToken = "SyncToken2"
                                    }
            };

        List<PushNotification> invalidPushNotificationList = new List<PushNotification>
            {
              new PushNotification  {
                                    ResourceUri = null,
                                    EventType = "eventType.KeyValueModified",
                                    SyncToken = "SyncToken1;sn=001"
                                    },
              new PushNotification  {
                                    ResourceUri = new Uri("https://store2.resource.io/kv/searchQuery2"),

									EventType = null,
                                    SyncToken = "SyncToken2"
                                    },
              new PushNotification  {
                                    ResourceUri = new Uri("https://store1.resource.io/kv/searchQuery1"),

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

		ConfigurationSetting FirstKeyValue => _kvCollection.First();

        [Fact]
        public void TryTryCreatePushNotification()
        {
            EventGridEvent eventGridEvent = new EventGridEvent()
            {
                Data = "{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"syncToken1;sn=001\"}",
                DataVersion = "2",
                EventTime = new DateTime(2021, 10, 6, 20, 08, 7),
                EventType = "eventType.KeyValueModified",
                Id = "some id",
                Subject = "https://store1.resource.io/kv/searchQuery1",
                Topic = "/subscriptions/subscription-value1/resourceGroups/resourceGroup1/providers/provider1/configurationstores/store1\\"
            };

			EventGridEvent badEventGridEventDataCapitalization = new EventGridEvent()
			{
				Data = "{\"Key\":\"searchQuery1\",\"Etag\":\"etagValue1\",\"SyncToken\":\"syncToken1;sn=001\"}",
				DataVersion = "2",
				EventTime = new DateTime(2021, 10, 6, 20, 08, 7),
				EventType = "eventType.KeyValueModified",
				Id = "some id",
				Subject = "https://store1.resource.io/kv/searchQuery1",
				Topic = "/subscriptions/subscription-value1/resourceGroups/resourceGroup1/providers/provider1/configurationstores/store1\\"
			};

			eventGridEvent.TryCreatePushNotification(out PushNotification pushNotification);

			Assert.Equal(eventGridEvent.EventType, pushNotification.EventType);
			Assert.Equal(new Uri(eventGridEvent.Subject), pushNotification.ResourceUri);
			Assert.Equal("syncToken1;sn=001", pushNotification.SyncToken);

			//Should Fail assertions since bad EventGridEvent
			badEventGridEventDataCapitalization.TryCreatePushNotification(out pushNotification);
			Assert.False(IsPushNotificationValid(pushNotification));
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
					options.Client = mockClient.Object;
					options.Select("*");
					options.ConfigureRefresh(refreshOptions =>
					{
						refreshOptions.Register("TestKey1", "label")
							.SetCacheExpiration(TimeSpan.FromDays(30));
					});
					refresher = options.GetRefresher();
				})
				.Build();

			foreach (PushNotification invalidPushNotification in invalidPushNotificationList)
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

			var config = new ConfigurationBuilder()
				.AddAzureAppConfiguration(options =>
				{
					options.Client = mockClient.Object;
					options.Select("*");
					options.ConfigureRefresh(refreshOptions =>
					{
						refreshOptions.Register("TestKey1", "label")
							.SetCacheExpiration(TimeSpan.FromDays(30));
					});
					refresher = options.GetRefresher();
				})
				.Build();

			foreach (PushNotification pushNotification in pushNotificationList)
			{
				refresher.ProcessPushNotification(pushNotification, TimeSpan.FromSeconds(0));
				refresher.RefreshAsync().Wait();
			}

			mockClient.Verify(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(8));
			mockClient.Verify(c => c.UpdateSyncToken(It.IsAny<string>()), Times.Exactly(8));
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
					options.Client = mockClient.Object;
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

			refresher.ProcessPushNotification(pushNotificationList.First(), TimeSpan.FromSeconds(0));
			refresher.RefreshAsync().Wait();

			Assert.Equal("newValue1", config["TestKey1"]);
		}

		private bool IsPushNotificationValid(PushNotification pn)
        {
			return pn != null && pn.SyncToken != null && pn.ResourceUri != null && pn.EventType != null;
        }

		private Mock<ConfigurationClient> GetMockConfigurationClient()
		{
			var mockResponse = new Mock<Response>();
			var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());

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
