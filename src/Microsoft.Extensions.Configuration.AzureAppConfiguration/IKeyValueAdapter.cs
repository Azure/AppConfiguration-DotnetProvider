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
    internal interface IKeyValueAdapter
    {
        Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Uri endpoint, Logger logger, CancellationToken cancellationToken);

        bool CanProcess(ConfigurationSetting setting);

        void InvalidateCache(ConfigurationSetting setting = null);

        bool NeedsRefresh();

        void ResetState();
    }
}
