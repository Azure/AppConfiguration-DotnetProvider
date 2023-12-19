// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IConfigurationClientManager
    {
        IEnumerable<ConfigurationClient> GetClients();

        bool UpdateSyncToken(Uri endpoint, string syncToken);

        Uri GetEndpointForClient(ConfigurationClient client);
    }
}
