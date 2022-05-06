// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IConfigurationClientProvider
    {
        IEnumerable<ConfigurationClient> GetClients();

        void UpdateClientStatus(ConfigurationClient client, bool successful);

        bool UpdateSyncToken(Uri endpoint, String syncToken);
    }
}
