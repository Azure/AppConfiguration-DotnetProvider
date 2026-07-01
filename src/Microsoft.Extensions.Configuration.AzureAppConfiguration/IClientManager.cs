// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IClientManager
    {
        IEnumerable<ClientWrapper> GetClients();

        void RefreshClients();

        bool UpdateSyncToken(Uri endpoint, string syncToken);
    }
}
