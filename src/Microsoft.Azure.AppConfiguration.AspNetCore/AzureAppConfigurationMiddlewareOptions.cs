namespace Microsoft.Azure.AppConfiguration.AspNetCore
{
    /// <summary>
    /// Options used to configure the behavior of Azure App Configuration middleware.
    /// </summary>
    public class AzureAppConfigurationMiddlewareOptions
    {
        /// <summary>
        /// Determines whether the middleware awaits the refresh operation during a request.
        /// Enable this option for scenarios where the refresh operation must be completed before the request is processed.
        /// </summary>
        public bool AwaitRefresh { get; set; }
    }
}
