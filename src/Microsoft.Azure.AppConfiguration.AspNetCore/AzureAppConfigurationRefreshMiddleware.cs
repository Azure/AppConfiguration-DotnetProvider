using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.AppConfiguration.AspNetCore
{
    /// <summary>
    /// Middleware for Azure App Configuration to use activity-based refresh for key-values registered in the provider.
    /// </summary>
    class AzureAppConfigurationRefreshMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AzureAppConfigurationMiddlewareOptions _options;
        public IList<IConfigurationRefresher> Refreshers { get; private set; }

        public AzureAppConfigurationRefreshMiddleware(
            RequestDelegate next,
            IConfiguration configuration,
            IOptions<AzureAppConfigurationMiddlewareOptions> options,
            Action<AzureAppConfigurationMiddlewareOptions> optionsInitializer = null)
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

            _options = options.Value ?? new AzureAppConfigurationMiddlewareOptions();
            optionsInitializer?.Invoke(_options);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            foreach (var refresher in Refreshers)
            {
                Task refreshTask = refresher.Refresh();

                if (_options.AwaitRefresh)
                {
                    await refreshTask;
                }
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context).ConfigureAwait(false);
        }
    }
}
