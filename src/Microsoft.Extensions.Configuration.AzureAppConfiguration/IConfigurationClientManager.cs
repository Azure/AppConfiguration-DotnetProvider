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

        void RefreshClients();

        bool UpdateSyncToken(Uri endpoint, string syncToken);

        /// <returns>null if client is not found</returns>
        Uri? GetEndpointForClient(ConfigurationClient client);
    }
}
