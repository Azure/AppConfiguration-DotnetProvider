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
        IAsyncEnumerable<ConfigurationClient> GetAvailableClients(CancellationToken cancellationToken);

        IAsyncEnumerable<ConfigurationClient> GetAllClients(CancellationToken cancellationToken);

        void UpdateClientStatus(ConfigurationClient client, bool successful);

        bool UpdateSyncToken(Uri endpoint, String syncToken);

        Uri GetEndpointForClient(ConfigurationClient client);
    }
}
