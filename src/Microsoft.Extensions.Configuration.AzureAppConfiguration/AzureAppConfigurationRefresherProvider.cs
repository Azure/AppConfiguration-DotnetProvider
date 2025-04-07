// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationRefresherProvider : IConfigurationRefresherProvider
    {
        private static readonly PropertyInfo _propertyInfo = typeof(ChainedConfigurationProvider).GetProperty("Configuration", BindingFlags.Public | BindingFlags.Instance);

        private readonly IConfiguration _configuration;
        private readonly ILoggerFactory _loggerFactory;
        private IEnumerable<IConfigurationRefresher> _refreshers;
        private bool _rediscoveredRefreshers = false;

        public IEnumerable<IConfigurationRefresher> Refreshers
        {
            get
            {
                if (!_rediscoveredRefreshers)
                {
                    _refreshers = DiscoverRefreshers();

                    _rediscoveredRefreshers = true;
                }

                return _refreshers;
            }
        }

        public AzureAppConfigurationRefresherProvider(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _loggerFactory = loggerFactory;

            _refreshers = DiscoverRefreshers();
        }

        private IEnumerable<IConfigurationRefresher> DiscoverRefreshers()
        {
            var configurationRoot = _configuration as IConfigurationRoot;
            var refreshers = new List<IConfigurationRefresher>();

            FindRefreshers(configurationRoot, _loggerFactory, refreshers);

            if (!refreshers.Any())
            {
                throw new InvalidOperationException("Unable to access the Azure App Configuration provider. Please ensure that it has been configured correctly.");
            }

            return refreshers;
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
