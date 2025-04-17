// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationHealthCheck : IHealthCheck
    {
        private AzureAppConfigurationProvider _provider = null;

        public AzureAppConfigurationHealthCheck(AzureAppConfigurationProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (!_provider.LastSuccessfulAttempt.HasValue)
            {
                return HealthCheckResult.Unhealthy(HealthCheckConstants.LoadNotCompletedMessage);
            }

            if (_provider.LastFailedAttempt.HasValue &&
                _provider.LastSuccessfulAttempt.Value < _provider.LastFailedAttempt.Value)
            {
                return HealthCheckResult.Unhealthy(HealthCheckConstants.RefreshFailedMessage);
            }

            return HealthCheckResult.Healthy();
        }
    }
}
