using System.Text.Json;
using System.Reflection;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Push Notification class
    /// </summary>
    public class PushNotification
    {			
		/// <summary>
		/// Resource URI/Topic
		/// </summary>
		public string Uri { get; set; }
        /// <summary>
        /// Sync Token from the provider
        /// </summary>
        public string SyncToken { get; set; }
        /// <summary>
        /// Event Type
        /// </summary>
        public string EventType { get; set; }


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
			catch (KeyNotFoundException) { }
			catch (AmbiguousMatchException) { }
			catch (ArgumentException) { }
			catch (InvalidOperationException) { }

			pushNotification.SyncToken = null;
			pushNotification.EventType= null;
			pushNotification.Uri = null;
			return false;
		}
	}
}