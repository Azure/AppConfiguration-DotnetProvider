// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.ConfigurationClients;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IConfigurationClientFactory
    {
        IConfigurationClient CreateConfigurationClient(string connectionString, AzureAppConfigurationOptions options);

        /// <summary>
        /// Creates the configuration client from the given list of endpoints, token credential and configuration client options.
        /// </summary>
        /// <param name="endpoints">The list of endpoints from where to get the configuration settings.</param>
        /// <param name="credential">The token credential used to access the configuration client.</param>
        /// <param name="options">The options used to create the configuration client.</param>
        /// <returns>The <see cref="FailOverSupportedConfigurationClient"/> used to interact with the configuration store and it's replicas.</returns>
        IConfigurationClient CreateConfigurationClient(IEnumerable<Uri> endpoints, TokenCredential credential, AzureAppConfigurationOptions options);
    }
}
