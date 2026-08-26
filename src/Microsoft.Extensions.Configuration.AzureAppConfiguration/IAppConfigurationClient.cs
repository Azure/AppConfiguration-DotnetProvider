// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// A single client abstraction for an Azure App Configuration endpoint that exposes the subset of
    /// operations the provider needs from both <see cref="ConfigurationClient"/> and
    /// <see cref="FeatureFlagClient"/>. The implementation holds both underlying SDK clients internally so
    /// that the provider can work with a single client per endpoint.
    /// </summary>
    internal interface IAppConfigurationClient
    {
        /// <summary>
        /// The endpoint of the Azure App Configuration store this client communicates with.
        /// </summary>
        Uri Endpoint { get; }

        AsyncPageable<ConfigurationSetting> GetConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken);

        AsyncPageable<ConfigurationSetting> CheckConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken);

        Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(string key, string label, CancellationToken cancellationToken);

        Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken);

        Task<Response<ConfigurationSnapshot>> GetSnapshotAsync(string snapshotName, CancellationToken cancellationToken);

        AsyncPageable<ConfigurationSetting> GetConfigurationSettingsForSnapshotAsync(string snapshotName, CancellationToken cancellationToken);

        AsyncPageable<FeatureFlag> GetFeatureFlagsAsync(FeatureFlagSelector selector, CancellationToken cancellationToken);

        void UpdateSyncToken(string syncToken);
    }
}
