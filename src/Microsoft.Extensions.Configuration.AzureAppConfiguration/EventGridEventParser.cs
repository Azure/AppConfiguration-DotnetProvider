using System.Text.Json;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
	/// <summary>
	/// Summary for PushNotificationParser
	/// </summary>
	public static class EventGridEventParser
	{
		/// <summary>
		///  Attempts to parse the message and populate pushNotification
		/// </summary>
		/// <param name="eventGridEvent"> Message Data from Event Grid</param>
		/// <param name="pushNotification"> out parameter which will try to be populated</param>
		/// <returns></returns>
		public static bool TryParseJson(string eventGridEvent, out PushNotification pushNotification)
		{
			pushNotification = new PushNotification();

			if (eventGridEvent == null)
			{
				return false;
			}

			try
			{
				JsonElement jsonMessage = JsonDocument.Parse(eventGridEvent).RootElement;

				pushNotification.SyncToken = jsonMessage.GetProperty("data").GetProperty("syncToken").GetString();
				pushNotification.EventType = jsonMessage.GetProperty("eventType").GetString();
				pushNotification.Uri = jsonMessage.GetProperty("topic").GetString();

				return true;
			}
			catch (JsonException) { }
			catch (KeyNotFoundException) { }
			catch (ArgumentException) { }
			catch (InvalidOperationException) { }

			pushNotification = null;
			return false;
		}
	}
}