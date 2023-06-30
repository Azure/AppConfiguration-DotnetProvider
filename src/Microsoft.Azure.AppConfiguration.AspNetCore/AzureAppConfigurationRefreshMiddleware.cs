// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System.Collections.Generic;
using System.Threading;
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
            //
            // Configuration refresh is meant to execute as an isolated background task.
            // To prevent access of request-based resources, such as HttpContext, we suppress the execution context within the refresh operation.
            using (var flowControl = ExecutionContext.SuppressFlow())
            {
                foreach (IConfigurationRefresher refresher in Refreshers)
                {
                    _ = Task.Run(() => refresher.TryRefreshAsync());
                }
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context).ConfigureAwait(false);
        }
    }
}
