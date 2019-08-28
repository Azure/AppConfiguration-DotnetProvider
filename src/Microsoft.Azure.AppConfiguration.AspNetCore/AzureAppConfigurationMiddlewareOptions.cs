namespace Microsoft.Azure.AppConfiguration.AspNetCore
{
    /// <summary>
    /// Options used to configure the behavior of Azure App Configuration middleware.
    /// </summary>
    public class AzureAppConfigurationMiddlewareOptions
    {
        /// <summary>
        /// Enables or disables whether the middleware awaits the refresh operation triggered on each request to complete.
        /// Enable this option for scenarios where you want to force the refresh operation to complete before each request is processed.
        /// </summary>
        public bool AwaitRefresh { get; set; }
    }
}
