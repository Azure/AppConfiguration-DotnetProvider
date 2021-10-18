using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Object containing three parameters: A string Uri, string SyncToken, and string EventType 
    /// </summary>
    public class PushNotification
    {			

		/// <summary>
		/// Subject
		/// </summary>
		public Uri ResourceUri { get; set; }

        /// <summary>
        /// The Synchronization Token to be added to the next request to the App Configuration Service.
        /// </summary>
        public string SyncToken { get; set; }

        /// <summary>
        /// Event Type
        /// </summary>
        public string EventType { get; set; }

	}
}