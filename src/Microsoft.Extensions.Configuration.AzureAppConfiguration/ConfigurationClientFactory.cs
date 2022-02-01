// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ConfigurationClientFactory : IConfigurationClientFactory
    {
        public IConfigurationClient CreateConfigurationClient(string connectionString, ConfigurationClientOptions clientOptions)
        {
            var configurationClient = new ConfigurationClient(connectionString, clientOptions);
            var endpoint = new Uri(ConnectionStringParser.Parse(connectionString, "Endpoint"));
            return new FailOverSupportedConfigurationClient(new List<LocalConfigurationClient>() { new LocalConfigurationClient(endpoint, configurationClient) });
        }

        public IConfigurationClient CreateConfigurationClient(IEnumerable<Uri> endpoints, TokenCredential credential, ConfigurationClientOptions clientOptions)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            var configurationClients = endpoints.Select(endpoint => new LocalConfigurationClient(endpoint, new ConfigurationClient(endpoint, credential, clientOptions)));

            return new FailOverSupportedConfigurationClient(configurationClients);
        }
    }
}
