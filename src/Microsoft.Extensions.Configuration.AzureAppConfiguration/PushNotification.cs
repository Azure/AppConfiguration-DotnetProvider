// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// An Object containing the details of a push Notification received from the Azure App Configuration service.
    /// </summary>
    public class PushNotification
    {
        /// <summary>
        /// The URI of the resource which triggered the <see cref="PushNotification"/>.
        /// </summary>
        public Uri ResourceUri { get; set; }

        /// <summary>
        /// The Synchronization Token to be added to the next request to the App Configuration Service.
        /// </summary>
        public string SyncToken { get; set; }

        /// <summary>
        /// The Type of Event which triggered the <see cref="PushNotification"/>.
        /// </summary>
        public string EventType { get; set; }

	}
}