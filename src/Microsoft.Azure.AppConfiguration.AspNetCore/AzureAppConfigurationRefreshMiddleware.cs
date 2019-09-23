using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Azure.AppConfiguration.AspNetCore
{
    /// <summary>
    /// Middleware for Azure App Configuration to use activity-based refresh for key-values registered in the provider.
    /// </summary>
    class AzureAppConfigurationRefreshMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly List<IConfigurationRefresher> _refreshers;

        public AzureAppConfigurationRefreshMiddleware(RequestDelegate next, IConfiguration configuration, IConfigurationRefresher refresher = null)
        {
            _next = next;
            _refreshers = new List<IConfigurationRefresher>();

            if (refresher == null)
            {
                var configurationRoot = configuration as IConfigurationRoot;

                foreach (var provider in configurationRoot?.Providers)
                {
                    if (provider is IConfigurationRefresher r)
                    {
                        _refreshers.Add(r);
                    }
                }

                if (!_refreshers.Any())
                {
                    throw new InvalidOperationException($"Unable to find an Azure App Configuration provider. Please ensure that it has been added to {typeof(IConfiguration)} within the service collection.");
                }
            }
            else
            {
                _refreshers.Add(refresher);
            }
        }

        public async Task InvokeAsync(HttpContext context)
        {
            _refreshers.ForEach(r => r.Refresh());

            // Call the next delegate/middleware in the pipeline
            await _next(context).ConfigureAwait(false);
        }
    }
}
