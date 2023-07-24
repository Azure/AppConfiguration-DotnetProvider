// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.AppConfiguration.Functions.Worker
{
    /// <summary>
    /// Middleware for Azure App Configuration to use activity-based refresh for key-values registered in the provider.
    /// </summary>
    internal class AzureAppConfigurationRefreshMiddleware : IFunctionsWorkerMiddleware
    {
        private IEnumerable<IConfigurationRefresher> Refreshers { get; }
        private DateTimeOffset refreshReadyTime = DateTimeOffset.UtcNow;

        public AzureAppConfigurationRefreshMiddleware(IConfigurationRefresherProvider refresherProvider)
        {
            Refreshers = refresherProvider.Refreshers;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
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

            await next(context).ConfigureAwait(false);
        }
    }
}
