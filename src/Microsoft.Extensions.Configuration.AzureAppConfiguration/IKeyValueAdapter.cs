// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IKeyValueAdapter
    {
        Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, CancellationToken cancellationToken);

        bool CanProcess(ConfigurationSetting setting);

        void InvalidateCache(ConfigurationSetting setting = null);

        bool NeedsRefresh();
    }
}
