// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods to configure <see cref="AzureAppConfigurationHealthCheck"/>.
    /// </summary>
    public static class AzureAppConfigurationHealthChecksBuilderExtensions
    {
        private static readonly bool _isProviderDisabled = ProviderToggleChecker.IsProviderDisabled();

        /// <summary>
        /// Add a health check for Azure App Configuration to given <paramref name="builder"/>.
        /// </summary>
        /// <param name="builder">The <see cref="IHealthChecksBuilder"/> to add <see cref="HealthCheckRegistration"/> to.</param>
        /// <param name="factory"> A factory to obtain <see cref="IConfiguration"/> instance.</param>
        /// <param name="name">The health check name.</param>
        /// <param name="failureStatus">The <see cref="HealthStatus"/> that should be reported when the health check fails.</param>
        /// <param name="tags">A list of tags that can be used to filter sets of health checks.</param>
        /// <param name="timeout">A <see cref="TimeSpan"/> representing the timeout of the check.</param>
        /// <returns>The provided health checks builder.</returns>
        public static IHealthChecksBuilder AddAzureAppConfiguration(
            this IHealthChecksBuilder builder,
            Func<IServiceProvider, IConfiguration> factory = default,
            string name = HealthCheckConstants.HealthCheckRegistrationName,
            HealthStatus failureStatus = default,
            IEnumerable<string> tags = default,
            TimeSpan? timeout = default)
        {
            IHealthCheck CreateHealthCheck(IServiceProvider sp)
            {
                if (_isProviderDisabled)
                {
                    return new AlwaysHealthyHealthCheck();
                }

                return new AzureAppConfigurationHealthCheck(
                    factory?.Invoke(sp) ?? sp.GetRequiredService<IConfiguration>());
            }

            return builder.Add(new HealthCheckRegistration(
                name ?? HealthCheckConstants.HealthCheckRegistrationName,
                CreateHealthCheck,
                failureStatus,
                tags,
                timeout));
        }
    }
}

