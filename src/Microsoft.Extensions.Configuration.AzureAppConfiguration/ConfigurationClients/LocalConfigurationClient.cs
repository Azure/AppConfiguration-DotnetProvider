// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.ConfigurationClients
{
    internal class LocalConfigurationClient
    {
        public LocalConfigurationClient(Uri endpoint, ConfigurationClient client)
        {
            this.Endpoint = endpoint;
            this.Client = client;
        }

        public ConfigurationClient Client { get; private set; }

        public Uri Endpoint { get; private set; }
    }
}
