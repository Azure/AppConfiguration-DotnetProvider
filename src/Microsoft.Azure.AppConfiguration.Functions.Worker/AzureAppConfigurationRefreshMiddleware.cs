// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.AppConfiguration.Functions.Worker
{
    /// <summary>
    /// Middleware for Azure App Configuration to use activity-based refresh for key-values registered in the provider.
    /// </summary>
    internal class AzureAppConfigurationRefreshMiddleware : IFunctionsWorkerMiddleware
    {
        public IEnumerable<IConfigurationRefresher> Refreshers { get; }

        public AzureAppConfigurationRefreshMiddleware(IConfigurationRefresherProvider refresherProvider)
        {
            Refreshers = refresherProvider.Refreshers;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            foreach (IConfigurationRefresher refresher in Refreshers)
            {
                _ = refresher.TryRefreshAsync();
            }

            await next(context).ConfigureAwait(false);
        }
    }
}
