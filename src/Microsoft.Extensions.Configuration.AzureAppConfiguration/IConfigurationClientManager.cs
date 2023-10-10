// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IConfigurationClientManager
    {
        Task<IEnumerable<ConfigurationClient>> GetAvailableClients(DateTimeOffset time, CancellationToken cancellationToken);

        void UpdateClientStatus(ConfigurationClient client, bool successful);

        bool UpdateSyncToken(Uri endpoint, String syncToken);

        Uri GetEndpointForClient(ConfigurationClient client);
    }
}
