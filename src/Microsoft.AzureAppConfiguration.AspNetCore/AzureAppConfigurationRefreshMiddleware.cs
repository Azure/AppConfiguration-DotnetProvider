using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.AzureAppConfiguration.AspNetCore
{
    /// <summary>
    /// Middleware for Azure App Configuration to use activity-based refresh for key-values registered in the provider.
    /// </summary>
    class AzureAppConfigurationRefreshMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IList<IConfigurationRefresher> _refreshers;

        public AzureAppConfigurationRefreshMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _refreshers = new List<IConfigurationRefresher>();

            var configurationRoot = (IConfigurationRoot)configuration;
            var providers = configurationRoot.Providers;

            foreach (var provider in providers)
            {
                if (provider is IConfigurationRefresher refresher)
                {
                    _refreshers.Add(refresher);
                }
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            foreach (var refresher in _refreshers)
            {
                refresher.Refresh();
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }
    }
}
