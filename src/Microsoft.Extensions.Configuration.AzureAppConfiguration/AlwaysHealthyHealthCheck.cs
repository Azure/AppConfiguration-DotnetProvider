// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal sealed class AlwaysHealthyHealthCheck : IHealthCheck
    {
        private static readonly Task<HealthCheckResult> _healthyResult = Task.FromResult(HealthCheckResult.Healthy());

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return _healthyResult;
        }
    }
}
