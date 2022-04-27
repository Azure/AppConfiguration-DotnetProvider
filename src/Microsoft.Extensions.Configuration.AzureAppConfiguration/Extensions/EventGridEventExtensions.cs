// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Messaging.EventGrid;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System;
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    /// <summary>
    /// This class offers extensions for EventGridEvents.
    /// </summary>
    public static class EventGridEventExtensions
    {
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
                NotificationDataV2 notificationData;

                try
                {
                    notificationData = JsonSerializer.Deserialize<NotificationDataV2>(eventGridEvent.Data.ToString());
                }
                catch (JsonException)
                {
                    return false;
                }

                if (notificationData != null &&
                   !string.IsNullOrWhiteSpace(notificationData.Key) &&
                   !string.IsNullOrWhiteSpace(notificationData.Etag) &&
                   !string.IsNullOrWhiteSpace(notificationData.SyncToken))
                {
                    pushNotification = new PushNotification()
                    {
                        SyncToken = notificationData.SyncToken,
                        EventType = eventGridEvent.EventType,
                        ResourceUri = resourceUri,
                        Key = notificationData.Key,
                        Label = notificationData.Label,
                        ETag = notificationData.Etag
                    };

                    return true;
                }
            }

            return false;
        }
    }
}