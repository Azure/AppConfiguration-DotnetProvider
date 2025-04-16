// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationHealthCheck : IHealthCheck
    {
        private static readonly PropertyInfo _propertyInfo = typeof(ChainedConfigurationProvider).GetProperty("Configuration", BindingFlags.Public | BindingFlags.Instance);
        private readonly List<AzureAppConfigurationProvider> _providers = new List<AzureAppConfigurationProvider>();

        public AzureAppConfigurationHealthCheck(IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var configurationRoot = configuration as IConfigurationRoot;
            FindProviders(configurationRoot);
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (!_providers.Any())
            {
                return HealthCheckResult.Unhealthy(HealthCheckConstants.NoProviderFoundMessage);
            }

            foreach (var provider in _providers)
            {
                if (!provider.LastSuccessfulAttempt.HasValue)
                {
                    return HealthCheckResult.Unhealthy(HealthCheckConstants.LoadNotCompletedMessage);
                }

                if (provider.LastFailedAttempt.HasValue &&
                    provider.LastSuccessfulAttempt.Value < provider.LastFailedAttempt.Value)
                {
                    return HealthCheckResult.Unhealthy(HealthCheckConstants.RefreshFailedMessage);
                }
            }

            return HealthCheckResult.Healthy();
        }

        private void FindProviders(IConfigurationRoot configurationRoot)
        {
            if (configurationRoot != null)
            {
                foreach (IConfigurationProvider provider in configurationRoot.Providers)
                {
                    if (provider is AzureAppConfigurationProvider appConfigurationProvider)
                    {
                        _providers.Add(appConfigurationProvider);
                    }
                    else if (provider is ChainedConfigurationProvider chainedProvider)
                    {
                        if (_propertyInfo != null)
                        {
                            var chainedProviderConfigurationRoot = _propertyInfo.GetValue(chainedProvider) as IConfigurationRoot;
                            FindProviders(chainedProviderConfigurationRoot);
                        }
                    }
                }
            }
        }
    }
}
