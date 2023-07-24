// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
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
        private DateTimeOffset refreshReadyTime = DateTimeOffset.UtcNow;

        public AzureAppConfigurationRefreshMiddleware(RequestDelegate next, IConfigurationRefresherProvider refresherProvider)
        {
            _next = next;
            Refreshers = refresherProvider.Refreshers;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (refreshReadyTime <= DateTimeOffset.UtcNow)
            {
                //
                // Configuration refresh is meant to execute as an isolated background task.
                // To prevent access of request-based resources, such as HttpContext, we suppress the execution context within the refresh operation.
                using (AsyncFlowControl flowControl = ExecutionContext.SuppressFlow())
                {
                    foreach (IConfigurationRefresher refresher in Refreshers)
                    {
                        _ = Task.Run(() => refresher.TryRefreshAsync());
                    }
                }

                refreshReadyTime = DateTimeOffset.UtcNow.Add(TimeSpan.FromSeconds(1));
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context).ConfigureAwait(false);
        }
    }
}
