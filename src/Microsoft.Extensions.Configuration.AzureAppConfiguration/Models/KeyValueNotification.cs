// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    /// <summary>
    /// An Object containing the details of a push Notification received from the Azure App Configuration service specificaly for the KeyValueModified and KeyValueDeleted event type.
    /// </summary>
    public class KeyValueNotification : PushNotification
    {
        /// <summary>
        /// Key within the data sent by the eventgrid
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Label within the data sent by the eventgrid
        /// </summary>
        public string Label { get; set; } = LabelFilter.Null;

        /// <summary>
        /// Etag within the data sent by the eventgrid
        /// </summary>
        public string Etag { get; set; }
    }
}
