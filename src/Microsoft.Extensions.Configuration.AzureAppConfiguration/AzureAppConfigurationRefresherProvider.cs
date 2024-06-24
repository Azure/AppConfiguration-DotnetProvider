// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationRefresherProvider : IConfigurationRefresherProvider
    {
        private static readonly PropertyInfo _propertyInfo = typeof(ChainedConfigurationProvider).GetProperty("Configuration", BindingFlags.Public | BindingFlags.Instance);

        public IEnumerable<IConfigurationRefresher> Refreshers { get; }

        public AzureAppConfigurationRefresherProvider(IConfiguration configuration, ILoggerFactory _loggerFactory)
        {
            var configurationRoot = configuration as IConfigurationRoot;
            var refreshers = new List<IConfigurationRefresher>();

            FindRefreshers(configurationRoot, _loggerFactory, refreshers);

            if (!refreshers.Any())
            {
                throw new InvalidOperationException("Unable to access the Azure App Configuration provider. Please ensure that it has been configured correctly.");
            }

            Refreshers = refreshers;
        }

        private void FindRefreshers(IConfigurationRoot configurationRoot, ILoggerFactory loggerFactory, List<IConfigurationRefresher> refreshers)
        {
            if (configurationRoot != null)
            {
                foreach (IConfigurationProvider provider in configurationRoot.Providers)
                {
                    if (provider is AzureAppConfigurationProvider appConfigurationProvider)
                    {
                        appConfigurationProvider.LoggerFactory = loggerFactory;
                        refreshers.Add(appConfigurationProvider);
                    }
                    else if (provider is ChainedConfigurationProvider chainedProvider)
                    {
                        if (_propertyInfo != null)
                        {
                            var chainedProviderConfigurationRoot = _propertyInfo.GetValue(chainedProvider) as IConfigurationRoot;
                            FindRefreshers(chainedProviderConfigurationRoot, loggerFactory, refreshers);
                        }
                    }
                }
            }
        }
    }
}
