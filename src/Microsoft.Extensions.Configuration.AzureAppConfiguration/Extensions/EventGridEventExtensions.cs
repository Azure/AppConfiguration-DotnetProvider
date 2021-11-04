// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Messaging.EventGrid;
using System;
using System.Reflection;
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
	/// <summary>
	/// Extension of the EventGridEvent class for creating <see cref="PushNotification"/> object from an <see cref="EventGridEvent "/> object.
	/// </summary>
	public static class EventGridEventExtensions
    {
		private const string SyncTokenPropertyName = "syncToken";

		/// <summary>
		/// This method uses an EventGridEvent from EventGrid and tries to create a <see cref="PushNotification"/>
		/// </summary>
		/// <param name="eventGridEvent"> EventGridEvent from EventGrid</param>
		/// <param name="pushNotification"> out parameter set up in this method</param>
		/// <returns></returns>
		public static bool TryCreatePushNotification(this EventGridEvent eventGridEvent, out PushNotification pushNotification)
		{
			pushNotification = null;

			if (eventGridEvent == null || eventGridEvent.Data == null || eventGridEvent.EventType == null || eventGridEvent.Subject == null)
			{
				return false;
			}

			try
			{
				string syncToken = (JsonDocument.Parse(eventGridEvent.Data.ToString()).RootElement)
					.GetProperty(SyncTokenPropertyName).GetString();

				pushNotification = new PushNotification()
				{
					SyncToken = syncToken,
					EventType = eventGridEvent.EventType,
					ResourceUri = new Uri(eventGridEvent.Subject)
				};

				return true;
			}
			catch (JsonException) { }
			catch (ArgumentNullException) { }
			catch (ArgumentException) { }
			catch (AmbiguousMatchException) { }
			catch (InvalidOperationException) { }
			catch (UriFormatException) { }

			return false;
		}
	}
}