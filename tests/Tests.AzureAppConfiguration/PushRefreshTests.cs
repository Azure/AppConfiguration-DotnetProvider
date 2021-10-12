using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
//Microsoft.Extensions.Configuration.AzureAppConfiguration

namespace Tests.AzureAppConfiguration
{
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

		public List<PushNotification> CreatePushNotificationList()
		{
			List<PushNotification> pushNotificationList = new List<PushNotification>
			{
			  new PushNotification  {
									Uri = "/subscriptions/s/resourceGroups/rg/providers/p/configurationstores/s1",
									EventType = "eventType.KeyValueModified",
									SyncToken = "SyncToken1;sn=001"
									},
			  new PushNotification  {
									Uri = "/subscriptions/s/resourceGroups/rg/providers/p/configurationstores/s1",
									EventType = "eventType.KeyValueModified",
									SyncToken = "SyncToken2"
									},
			  new PushNotification  {
									Uri = "/subscriptions/s/resourceGroups/rg/providers/p/configurationstores/s1",
									EventType = "eventType.KeyValueDeleted",
									SyncToken = "SyncToken1;sn=001"
									},
			  new PushNotification  {
									Uri = "/subscriptions/s/resourceGroups/rg/providers/p/configurationstores/s1",
									EventType = "eventType.KeyValueDeleted",
									SyncToken = "SyncToken2"
									},
			  new PushNotification  {
									Uri = "/subscriptions/s/resourceGroups/rg/providers/p/configurationstores/s2",
									EventType = "eventType.KeyValueModified",
									SyncToken = "SyncToken1"
									},
			  new PushNotification  {
									Uri = "/subscriptions/s/resourceGroups/rg/providers/p/configurationstores/s2",
									EventType = "eventType.KeyValueModified",
									SyncToken = "SyncToken2"
									},
			  new PushNotification  {
									Uri = "/subscriptions/s/resourceGroups/rg/providers/p/configurationstores/s2",
									EventType = "eventType.KeyValueDeleted",
									SyncToken = "SyncToken1"
									},
			  new PushNotification  {
									Uri = "/subscriptions/s/resourceGroups/rg/providers/p/configurationstores/s2",
									EventType = "eventType.KeyValueDeleted",
									SyncToken = "SyncToken2"
									}
			};

			return pushNotificationList;
		}

		public List<PushNotification> CreateInvalidPushNotificationList()
		{
			List<PushNotification> pushNotificationList = new List<PushNotification>
			{
			  new PushNotification  {
									Uri = null,
									EventType = "eventType.KeyValueModified",
									SyncToken = "SyncToken1;sn=001"
									},
			  new PushNotification  {
									Uri = "/subscriptions/s/resourceGroups/rg/providers/p/configurationstores/s1",
									EventType = null,
									SyncToken = "SyncToken2"
									},
			  new PushNotification  {
									Uri = "/subscriptions/s/resourceGroups/rg/providers/p/configurationstores/s1",
									EventType = "eventType.KeyValueDeleted",
									SyncToken = null
									},
			  new PushNotification  {
									Uri = null,
									EventType = "eventType.KeyValueDeleted",
									SyncToken = null
									},
			  new PushNotification  {
									Uri = null,
									EventType = null,
									SyncToken = null
									}
			};

			return pushNotificationList;
		}


		[Fact]
		public void PushNotification_TryParseJson_LoadsValues()
		{
			//TODO: edge cases for parsing, passing null and check that a null exception is thrown
			//Extract all components of the sample messages into individual messages
			// something like this: Assert.Throws<AggregateException>(action);
			string sampleMessage1 = "{\"id\":\"id-value1\",\"topic\":\"/subscriptions/subscription-value1/resourceGroups/resourceGroup1/providers/provider1/configurationstores/store1\",\"subject\":\"https://store1.resource.io/kv/searchQuery1\",\"data\":{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"syncToken1;sn=001\"},\"eventType\":\"eventType.KeyValueModified\",\"dataVersion\":\"2\",\"metadataVersion\":\"1\",\"eventTime\":\"2021-10-06T20:08:07.2536025Z\"}";
			string syncToken1 = "syncToken1;sn=001";
			string uri1 = "/subscriptions/subscription-value1/resourceGroups/resourceGroup1/providers/provider1/configurationstores/store1";
			string eventType1 = "eventType.KeyValueModified";

			string sampleMessage2 = "{\"id\":\"id-value2\",\"topic\":\"/subscriptions/subscription-value2/resourceGroups/resourceGroup2/providers/provider2/configurationstores/store2\",\"subject\":\"https://store2.resource.io/kv/searchQuery2\",\"data\":{\"key\":\"searchQuery2\",\"etag\":\"etagValue2\",\"syncToken\":\"syncToken2;sn=002\"},\"eventType\":\"eventType.KeyValueDeleted\",\"dataVersion\":\"2\",\"metadataVersion\":\"1\",\"eventTime\":\"2021-10-06T20:08:07.2536025Z\"}";
			string syncToken2 = "syncToken2;sn=002";
			string uri2 = "/subscriptions/subscription-value2/resourceGroups/resourceGroup2/providers/provider2/configurationstores/store2";
			string eventType2 = "eventType.KeyValueDeleted";

			//Create the two Push Notifications to parse sample messages into
			PushNotification pushNotification1 = new PushNotification();
			PushNotification pushNotification2 = new PushNotification();

			//Parse the sampleMessages into the pushNotification
			EventGridEventParser.TryParseJson(sampleMessage1, out pushNotification1);
			EventGridEventParser.TryParseJson(sampleMessage2, out pushNotification2);

			Assert.Equal(pushNotification1.SyncToken, syncToken1);
			Assert.Equal(pushNotification1.Uri, uri1);
			Assert.Equal(pushNotification1.EventType, eventType1);

			Assert.Equal(pushNotification2.SyncToken, syncToken2);
			Assert.Equal(pushNotification2.Uri, uri2);
			Assert.Equal(pushNotification2.EventType, eventType2);
		}

		[Fact]
		public void PushNotification_TestNullPushNotificationProcessPushNotification()
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

			//Create list of PushNotifications with null Parameters
			List<PushNotification> invalidPushNotifications = CreateInvalidPushNotificationList();

			foreach (PushNotification invalidPushNotification in invalidPushNotifications)
            {
                try
                {
                    refresher.ProcessPushNotification(invalidPushNotification);
                    refresher.RefreshAsync().Wait();

                }
				//Should be caught in this block and continue to the next invalidNotification
                catch (ArgumentNullException) { Assert.True(true); continue; }

				//ProcessPushNotification did not throw any errors
				Assert.True(false);

            }
		}

		[Fact]
		public void PushNotification_TryParseJson_ParsesInvalidEventGridMessage()
		{
			//Extract all components of the sample messages into individual messages
			// something like this: Assert.Throws<AggregateException>(action);
			string emptyMessage = "";
			string nullMessage = null;

			string badSyncTokenMsg = "{\"id\":\"id-value1\",\"topic\":\"/subscriptions/subscription-value1/resourceGroups/resourceGroup1/providers/provider1/configurationstores/store1\",\"subject\":\"https://store1.resource.io/kv/searchQuery1\",\"data\":{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"syncToken1;error\"},\"eventType\":\"eventType.KeyValueModified\",\"dataVersion\":\"2\",\"metadataVersion\":\"1\",\"eventTime\":\"2021-10-06T20:08:07.2536025Z\"}";
			string noSyncTokenMsg   = "{\"id\":\"id-value1\",\"topic\":\"/subscriptions/subscription-value1/resourceGroups/resourceGroup1/providers/provider1/configurationstores/store1\",\"subject\":\"https://store1.resource.io/kv/searchQuery1\",\"data\":{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\"},\"eventType\":\"eventType.KeyValueModified\",\"dataVersion\":\"2\",\"metadataVersion\":\"1\",\"eventTime\":\"2021-10-06T20:08:07.2536025Z\"}";

			string badUriMsg = "{\"id\":\"id-value1\",\"topic\":\"/subscriptions/resourceGroups/resourceGroup1/providers/provider1/configurationstores/store1\",\"subject\":\"https://store1.resource.io/kv/searchQuery1\",\"data\":{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"syncToken1;sn=001\"},\"eventType\":\"eventType.KeyValueModified\",\"dataVersion\":\"2\",\"metadataVersion\":\"1\",\"eventTime\":\"2021-10-06T20:08:07.2536025Z\"}";
			string noUriMsg   = "{\"id\":\"id-value1\",\"subject\":\"https://store1.resource.io/kv/searchQuery1\",\"data\":{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"syncToken1;sn=001\"},\"eventType\":\"eventType.KeyValueModified\",\"dataVersion\":\"2\",\"metadataVersion\":\"1\",\"eventTime\":\"2021-10-06T20:08:07.2536025Z\"}";

			string badEventType = "{\"id\":\"id-value1\",\"topic\":\"/subscriptions/subscription-value1/resourceGroups/resourceGroup1/providers/provider1/configurationstores/store1\",\"subject\":\"https://store1.resource.io/kv/searchQuery1\",\"data\":{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"syncToken1;sn=001\"},\"eventType\":\"eventType.KeyValue\",\"dataVersion\":\"2\",\"metadataVersion\":\"1\",\"eventTime\":\"2021-10-06T20:08:07.2536025Z\"}";
			string noEventTypeMsg   = "{\"id\":\"id-value1\",\"topic\":\"/subscriptions/subscription-value1/resourceGroups/resourceGroup1/providers/provider1/configurationstores/store1\",\"subject\":\"https://store1.resource.io/kv/searchQuery1\",\"data\":{\"key\":\"searchQuery1\",\"etag\":\"etagValue1\",\"syncToken\":\"syncToken1;sn=001\"},\"dataVersion\":\"2\",\"metadataVersion\":\"1\",\"eventTime\":\"2021-10-06T20:08:07.2536025Z\"}";

			PushNotification pushNotification = new PushNotification();

			//Should returnFalse as empty or null string
			Assert.False(EventGridEventParser.TryParseJson(emptyMessage, out pushNotification));
			Assert.True(IsPushNotificationNull(pushNotification));
			Assert.False(EventGridEventParser.TryParseJson(nullMessage, out pushNotification));
			Assert.True(IsPushNotificationNull(pushNotification));

			//Should return true since the parameter is put into PushNotification
			//SDK will handle incorrect data/formatting in each parameter
			Assert.True(EventGridEventParser.TryParseJson(badSyncTokenMsg, out pushNotification));
			Assert.True(EventGridEventParser.TryParseJson(badUriMsg, out pushNotification));
			Assert.True(EventGridEventParser.TryParseJson(badEventType, out pushNotification));
			
			//These should return false as parameter was not found and put into pushNotification
			Assert.False(EventGridEventParser.TryParseJson(noSyncTokenMsg, out pushNotification));
			Assert.True(IsPushNotificationNull(pushNotification));
			Assert.False(EventGridEventParser.TryParseJson(noUriMsg, out pushNotification));
			Assert.True(IsPushNotificationNull(pushNotification));
			Assert.False(EventGridEventParser.TryParseJson(noEventTypeMsg, out pushNotification));
			Assert.True(IsPushNotificationNull(pushNotification));
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

			List<PushNotification> pushNotifications = CreatePushNotificationList();

			foreach (PushNotification pushNotification in pushNotifications)
			{
				refresher.ProcessPushNotification(pushNotification, TimeSpan.FromSeconds(0));
				refresher.RefreshAsync().Wait();
			}

			mockClient.Verify(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(8));
			mockClient.Verify(c => c.UpdateSyncToken(It.IsAny<string>()), Times.Exactly(8));
		}

		private bool IsPushNotificationNull(PushNotification pn)
        {
			return ((pn == null) || (pn.SyncToken == null || pn.EventType == null || pn.Uri == null)) ? true : false;
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
