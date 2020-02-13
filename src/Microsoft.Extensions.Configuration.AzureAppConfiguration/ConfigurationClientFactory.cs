// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ConfigurationClientFactory : IConfigurationClientFactory
    {
        public ConfigurationClient CreateConfigurationClient(string connectionString)
        {
            var clientOptions = AzureAppConfigurationProvider.GetClientOptions();
            return new ConfigurationClient(connectionString, clientOptions); 
        }

        public ConfigurationClient CreateConfigurationClient(Uri endpoint, TokenCredential credential)
        {
            var clientOptions = AzureAppConfigurationProvider.GetClientOptions();
            return new ConfigurationClient(endpoint, credential, clientOptions);
        }
    }
}
