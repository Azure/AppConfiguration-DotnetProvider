// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Messaging.EventGrid;
using System;
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
	/// <summary>
	/// This class offers extensions for EventGridEvents.
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

			if (eventGridEvent.Data == null || eventGridEvent.EventType == null || eventGridEvent.Subject == null)
			{
				return false;
			}

			Uri resourceUri;

			if (!Uri.TryCreate(eventGridEvent.Subject, UriKind.Absolute, out resourceUri))
            {
				return false;
            }

			try
			{
				JsonElement eventGridEventData = JsonDocument.Parse(eventGridEvent.Data.ToString()).RootElement;

				if (eventGridEventData.ValueKind == JsonValueKind.Object &&
					eventGridEventData.TryGetProperty(SyncTokenPropertyName, out JsonElement syncTokenJson))
                {
					pushNotification = new PushNotification()
					{
						SyncToken = syncTokenJson.GetString(),
						EventType = eventGridEvent.EventType,
						ResourceUri = resourceUri
					};

					return true;
				}
            }
            catch (JsonException) { }

			return false;
		}
	}
}