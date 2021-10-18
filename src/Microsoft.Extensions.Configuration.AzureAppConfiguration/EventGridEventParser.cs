using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
	/// <summary>
	/// EventGridEventParser contains the method to parse a Json EventGrid Message Notification
	/// </summary>
	public static class EventGridEventParser
	{
		/// <summary>
		///  Tries to parse the provided <paramref name="eventGridMessage"/> event message from Event Grid and 
		///  create a <see cref="PushNotification"/> object. Return value indicates whether the parsing succeeded
		///  or failed.
		/// </summary>
		/// <param name="eventGridMessage"> Message Data from Event Grid</param>
		/// <param name="pushNotification"> If this method returns true the <paramref name="pushNotification"/>object
		/// contains details parsed from <paramref name="eventGridMessage"/>. if this method returns false the 
		/// <paramref name="pushNotification"/> object is null.</param>
		/// <returns></returns>
		public static bool TryParseJson(string eventGridMessage, out PushNotification pushNotification)
		{
			pushNotification = null;

			if (eventGridMessage == null)
			{
				return false;
			}

			try
			{
				JsonElement jsonMessage = JsonDocument.Parse(eventGridMessage).RootElement;

				pushNotification = new PushNotification {
						SyncToken = jsonMessage.GetProperty("data").GetProperty("syncToken").GetString(),
						EventType = jsonMessage.GetProperty("eventType").GetString(),
						ResourceUri =  new Uri(jsonMessage.GetProperty("topic").GetString())};

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