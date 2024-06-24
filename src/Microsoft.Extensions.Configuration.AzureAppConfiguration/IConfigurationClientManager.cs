// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System;
using System.Collections.Generic;
using Azure.Data.AppConfiguration;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IConfigurationClientManager
    {
        IEnumerable<ConfigurationClient> GetClients();

        void RefreshClients();

        bool UpdateSyncToken(Uri endpoint, string syncToken);

        Uri GetEndpointForClient(ConfigurationClient client);
    }
}
