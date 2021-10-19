// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.AppConfiguration.AspNetCore
{
    /// <summary>
    /// Middleware for Azure App Configuration to use activity-based refresh for key-values registered in the provider.
    /// </summary>
    internal class AzureAppConfigurationRefreshMiddleware
    {
        private readonly RequestDelegate _next;
        public IEnumerable<IConfigurationRefresher> Refreshers { get; }

        public AzureAppConfigurationRefreshMiddleware(RequestDelegate next, IConfigurationRefresherProvider refresherProvider)
        {
            _next = next;
            Refreshers = refresherProvider.Refreshers;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            foreach (var refresher in Refreshers)
            {
                _ = refresher.TryRefreshAsync();
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context).ConfigureAwait(false);
        }
    }
}
