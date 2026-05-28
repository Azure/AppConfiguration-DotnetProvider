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

        // Pre-warm any per-setting state (e.g. Key Vault secret cache) before ProcessKeyValue is invoked
        // on each setting. Adapters with no pre-fetchable state can return a completed task.
        Task PreloadAsync(IEnumerable<ConfigurationSetting> settings, Logger logger, CancellationToken cancellationToken);

        bool CanProcess(ConfigurationSetting setting);

        void OnChangeDetected(ConfigurationSetting setting = null);

        void OnConfigUpdated();

        bool NeedsRefresh();
    }
}
