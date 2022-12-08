// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Messaging.EventGrid.SystemEvents;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationRefresherProvider : IConfigurationRefresherProvider
    {
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
                    if (provider is IConfigurationRefresher refresher)
                    {
                        // Use _loggerFactory only if LoggerFactory hasn't been set in AzureAppConfigurationOptions
                        if (refresher.LoggerFactory == null)
                        {
                            refresher.LoggerFactory = loggerFactory;
                        }

                        refreshers.Add(refresher);
                    }

                    if (provider is ChainedConfigurationProvider chainedProvider)
                    {
                        var root = chainedProvider.Configuration as IConfigurationRoot;
                        FindRefreshers(root, loggerFactory, refreshers);
                    }
                }
            }
        }
    }
}
