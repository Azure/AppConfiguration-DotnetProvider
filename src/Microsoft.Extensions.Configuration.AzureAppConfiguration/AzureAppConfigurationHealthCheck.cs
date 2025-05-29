// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationHealthCheck : IHealthCheck
    {
        private static readonly PropertyInfo _propertyInfo = typeof(ChainedConfigurationProvider).GetProperty("Configuration", BindingFlags.Public | BindingFlags.Instance);
        private readonly IEnumerable<IHealthCheck> _healthChecks;

        public AzureAppConfigurationHealthCheck(IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var healthChecks = new List<IHealthCheck>();
            var configurationRoot = configuration as IConfigurationRoot;
            FindHealthChecks(configurationRoot, healthChecks);

            _healthChecks = healthChecks;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (!_healthChecks.Any())
            {
                return HealthCheckResult.Unhealthy(HealthCheckConstants.NoProviderFoundMessage);
            }

            foreach (IHealthCheck healthCheck in _healthChecks)
            {
                var result = await healthCheck.CheckHealthAsync(context, cancellationToken).ConfigureAwait(false);

                if (result.Status == HealthStatus.Unhealthy)
                {
                    return result;
                }
            }

            return HealthCheckResult.Healthy();
        }

        private void FindHealthChecks(IConfigurationRoot configurationRoot, List<IHealthCheck> healthChecks)
        {
            if (configurationRoot != null)
            {
                foreach (IConfigurationProvider provider in configurationRoot.Providers)
                {
                    if (provider is AzureAppConfigurationProvider appConfigurationProvider)
                    {
                        healthChecks.Add(appConfigurationProvider);
                    }
                    else if (provider is ChainedConfigurationProvider chainedProvider)
                    {
                        if (_propertyInfo != null)
                        {
                            var chainedProviderConfigurationRoot = _propertyInfo.GetValue(chainedProvider) as IConfigurationRoot;
                            FindHealthChecks(chainedProviderConfigurationRoot, healthChecks);
                        }
                    }
                }
            }
        }
    }
}
