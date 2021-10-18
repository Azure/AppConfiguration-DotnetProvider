using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Object containing three parameters: A Uri subject, string SyncToken, and string EventType
    /// for an App Configuration store.
    /// </summary>
    public class PushNotification
    {			

		/// <summary>
		/// Uri Subject
		/// </summary>
		public Uri ResourceUri { get; set; }

        /// <summary>
        /// The Synchronization Token to be added to the next request to the App Configuration Service.
        /// </summary>
        public string SyncToken { get; set; }

        /// <summary>
        /// Event Type of Event Grid Message
        /// </summary>
        public string EventType { get; set; }

	}
}