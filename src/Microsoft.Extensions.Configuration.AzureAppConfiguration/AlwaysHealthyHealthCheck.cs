// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AlwaysHealthyHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}
