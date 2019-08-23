namespace Microsoft.Azure.AppConfiguration.AspNetCore
{
    /// <summary>
    /// Options used to configure the behavior of Azure App Configuration middleware.
    /// </summary>
    public class AzureAppConfigurationMiddlewareOptions
    {
        internal bool WaitForRefreshCompletion { get; private set; }

        /// <summary>
        /// Enables or disables whether the middleware waits for the refresh operation triggered on each request to complete.
        /// Enable this option for demo scenarios to force the refresh operation to complete before each request is processed.
        /// </summary>
        /// <param name="waitForRefreshCompletion"> If true, the middleware awaits the execution of the refresh operation.</param>
        public AzureAppConfigurationMiddlewareOptions SetWaitForRefresh(bool waitForRefreshCompletion)
        {
            WaitForRefreshCompletion = waitForRefreshCompletion;
            return this;
        }
    }
}
