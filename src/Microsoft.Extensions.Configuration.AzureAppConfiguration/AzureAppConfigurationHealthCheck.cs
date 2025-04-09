// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationHealthCheck : IConfigurationHealthCheck
    {
        private AzureAppConfigurationProvider _provider = null;

        internal void SetProvider(AzureAppConfigurationProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (_provider == null)
            {
                return HealthCheckResult.Unhealthy("Configuration provider is not set.");
            }

            if (!_provider.LastSuccessfulAttempt.HasValue)
            {
                return HealthCheckResult.Unhealthy("The initial load is not completed.");
            }

            if (_provider.LastFailedAttempt.HasValue &&
                _provider.LastSuccessfulAttempt.Value < _provider.LastFailedAttempt.Value)
            {
                return HealthCheckResult.Unhealthy("The last refresh attempt failed.");
            }

            return HealthCheckResult.Healthy();
        }
    }
}
