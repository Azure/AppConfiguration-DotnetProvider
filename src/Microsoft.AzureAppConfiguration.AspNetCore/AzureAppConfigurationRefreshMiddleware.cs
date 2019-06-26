using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
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
        public IList<IConfigurationRefresher> Refreshers { get; private set; }

        public AzureAppConfigurationRefreshMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            Refreshers = new List<IConfigurationRefresher>();
            var configurationRoot = configuration as IConfigurationRoot;

            if (configurationRoot == null)
            {
                throw new InvalidOperationException("Unable to access the Azure App Configuration provider. Please ensure that it has been configured correctly.");
            }

            foreach (var provider in configurationRoot.Providers)
            {
                if (provider is IConfigurationRefresher refresher)
                {
                    Refreshers.Add(refresher);
                }
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            foreach (var refresher in Refreshers)
            {
                refresher.Refresh();
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }
    }
}
