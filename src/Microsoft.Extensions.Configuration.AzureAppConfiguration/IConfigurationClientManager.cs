// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IConfigurationClientManager
    {
        IEnumerable<ConfigurationClient> GetClients();

        public void RefreshClients();

        bool UpdateSyncToken(Uri endpoint, string syncToken);

        Uri GetEndpointForClient(ConfigurationClient client);
    }
}
