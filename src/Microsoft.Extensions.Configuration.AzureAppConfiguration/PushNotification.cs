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
	}
}