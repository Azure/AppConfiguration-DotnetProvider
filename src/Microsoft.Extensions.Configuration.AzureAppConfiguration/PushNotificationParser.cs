using System.Text.Json;
using System.Reflection;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
	/// <summary>
	/// Summary for PushNotificationParser
	/// </summary>
	public static class PushNotificationParser
	{
		/// <summary>
		///  Attempts to parse the message and populate pushNotification
		/// </summary>
		/// <param name="message"> Message Data returned from the provider</param>
		/// <param name="pushNotification"> out parameter which will try to be populated</param>
		/// <returns></returns>
		public static bool TryParse(string message, out PushNotification pushNotification)
		{
			pushNotification = new PushNotification();

			if (message == null)
			{
				return false;
			}

			try
			{
				JsonElement jsonMessage = JsonDocument.Parse(message).RootElement;

				pushNotification.SyncToken = jsonMessage.GetProperty("data").GetProperty("syncToken").GetString();
				pushNotification.EventType = jsonMessage.GetProperty("eventType").GetString();
				pushNotification.Uri = jsonMessage.GetProperty("topic").GetString();

				return true;
			}
			catch (JsonException) { }
			catch (AmbiguousMatchException) { }
			catch (ArgumentException) { }
			catch (InvalidOperationException) { }

			return false;
		}
	}
}