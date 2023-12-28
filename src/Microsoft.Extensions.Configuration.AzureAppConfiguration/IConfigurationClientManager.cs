// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IConfigurationClientManager
    {
        IEnumerable<ConfigurationClient> GetAvailableClients();

        void UpdateClientStatus(ConfigurationClient client, bool successful);

        void UpdateStartupClientsStatus(IEnumerable<ConfigurationClient> clients, DateTimeOffset dateTime, Stopwatch startupStopwatch, bool successful);

        bool UpdateSyncToken(Uri endpoint, String syncToken);

        Uri GetEndpointForClient(ConfigurationClient client);
    }
}
