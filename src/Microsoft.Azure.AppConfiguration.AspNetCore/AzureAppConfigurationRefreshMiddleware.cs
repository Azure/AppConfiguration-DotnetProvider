using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System.Threading.Tasks;

namespace Microsoft.Azure.AppConfiguration.AspNetCore
{
    /// <summary>
    /// Middleware for Azure App Configuration to use activity-based refresh for key-values registered in the provider.
    /// </summary>
    class AzureAppConfigurationRefreshMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfigurationRefresher _refresher;

        public AzureAppConfigurationRefreshMiddleware(RequestDelegate next, IConfigurationRefresher refresher)
        {
            _next = next;
            _refresher = refresher;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            _refresher.Refresh();

            // Call the next delegate/middleware in the pipeline
            await _next(context).ConfigureAwait(false);
        }
    }
}
