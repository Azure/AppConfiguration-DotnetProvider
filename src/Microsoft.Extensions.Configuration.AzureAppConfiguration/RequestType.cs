namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Types of read requests.
    /// </summary>
    internal enum RequestType
    {
        /// <summary>
        /// Default type. Also used to imply not to log the type of request.
        /// </summary>
        None,

        /// <summary>
        /// Indicate watch/observe type request.
        /// </summary>
        Watch,

        /// <summary>
        /// Indicate read for app initialization/startup.
        /// </summary>
        Startup
    }
}
