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
        /// Tries to create the <see cref="PushNotification"/> object from the details in <see cref="EventGridEvent"/> object. Return value indicates whether the operation succeeded or failed.
        /// </summary>
        /// <param name="eventGridEvent"> EventGridEvent from EventGrid</param>
        /// <param name="pushNotification"> If this method returns true, the pushNotification  object contains details populated from eventGridEvent. If this method returns false, the pushNotification object is null. </param>
        /// <returns></returns>
        public static bool TryCreatePushNotification(this EventGridEvent eventGridEvent, out PushNotification pushNotification)
        {
            pushNotification = null;

            if (eventGridEvent.Data == null || eventGridEvent.EventType == null || eventGridEvent.Subject == null)
            {
                return false;
            }

            if (Uri.TryCreate(eventGridEvent.Subject, UriKind.Absolute, out Uri resourceUri))
            {
                JsonElement eventGridEventData;

                try
                {
                    eventGridEventData = JsonDocument.Parse(eventGridEvent.Data.ToString()).RootElement;
                }
                catch (JsonException)
                {
                    return false;
                }

                if (eventGridEventData.ValueKind == JsonValueKind.Object &&
                    eventGridEventData.TryGetProperty(SyncTokenPropertyName, out JsonElement syncTokenJson) &&
                    syncTokenJson.ValueKind == JsonValueKind.String)
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

            return false;
        }
    }
}