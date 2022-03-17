// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.ConfigurationClients;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ConfigurationClientFactory : IConfigurationClientFactory
    {
        public IConfigurationClient CreateConfigurationClient(string connectionString, AzureAppConfigurationOptions options)
        {
            return new LocalConfigurationClient(connectionString, options);
        }

        public IConfigurationClient CreateConfigurationClient(IEnumerable<Uri> endpoints, TokenCredential credential, AzureAppConfigurationOptions options)
        {
            if (endpoints == null || endpoints.Count() < 1)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }
            if (credential == null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            return new LocalConfigurationClient(endpoints, credential, options);
        }
    }
}
