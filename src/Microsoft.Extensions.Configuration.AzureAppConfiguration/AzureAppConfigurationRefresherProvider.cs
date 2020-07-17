// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    class AzureAppConfigurationRefresherProvider : IConfigurationRefresherProvider
    {
        public IEnumerable<IConfigurationRefresher> Refreshers { get; }

        public AzureAppConfigurationRefresherProvider(IConfiguration configuration)
        {
            var configurationRoot = configuration as IConfigurationRoot;

            if (configurationRoot == null)
            {
                throw new InvalidOperationException("Unable to access the Azure App Configuration provider. Please ensure that it has been configured correctly.");
            }

            var refreshers = new List<IConfigurationRefresher>();

            foreach (IConfigurationProvider provider in configurationRoot.Providers)
            {
                if (provider is IConfigurationRefresher refresher)
                {
                    refreshers.Add(refresher);
                }
            }

            Refreshers = refreshers;
        }
    }
}
