// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class LocalConfigurationClient
    {
        public LocalConfigurationClient(Uri Endpoint, ConfigurationClient client)
        {
            this.Endpoint = Endpoint;
            this.Client = client;
        }

        public ConfigurationClient Client { get; private set; }

        public Uri Endpoint { get; private set; }
    }
}
