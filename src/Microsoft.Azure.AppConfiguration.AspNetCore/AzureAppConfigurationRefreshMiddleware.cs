// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace Microsoft.Azure.AppConfiguration.AspNetCore
{
    /// <summary>
    /// Middleware for Azure App Configuration to use activity-based refresh for key-values registered in the provider.
    /// </summary>
    internal class AzureAppConfigurationRefreshMiddleware
    {
        // The minimum refresh interval on the configuration provider is 1 second, so refreshing more often is unnecessary
        private static readonly long MinimumRefreshInterval = TimeSpan.FromSeconds(1).Ticks;
        private readonly RequestDelegate _next;
        private long _refreshReadyTime = DateTimeOffset.UtcNow.Ticks;

        public IEnumerable<IConfigurationRefresher> Refreshers { get; }

        public AzureAppConfigurationRefreshMiddleware(RequestDelegate next, IConfigurationRefresherProvider refresherProvider)
        {
            _next = next;
            Refreshers = refresherProvider.Refreshers;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            long utcNow = DateTimeOffset.UtcNow.Ticks;

            long refreshReadyTime = Interlocked.Read(ref _refreshReadyTime);

            if (refreshReadyTime <= utcNow &&
                Interlocked.CompareExchange(ref _refreshReadyTime, utcNow + MinimumRefreshInterval, refreshReadyTime) == refreshReadyTime)
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
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context).ConfigureAwait(false);
        }
    }
}
