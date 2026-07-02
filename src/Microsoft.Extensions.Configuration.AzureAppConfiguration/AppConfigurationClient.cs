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
    /// The default <see cref="IAppConfigurationClient"/> implementation. It holds a
    /// <see cref="ConfigurationClient"/> for key-values (including classic feature flags) and a
    /// <see cref="FeatureFlagClient"/> for feature flags served by the standalone feature-flag endpoint,
    /// both targeting the same <see cref="Endpoint"/>.
    /// </summary>
    internal class AppConfigurationClient : IAppConfigurationClient
    {
        private readonly ConfigurationClient _configurationClient;
        private readonly FeatureFlagClient _featureFlagClient;

        public AppConfigurationClient(Uri endpoint, ConfigurationClient configurationClient)
        {
            Endpoint = endpoint;
            _configurationClient = configurationClient;
        }

        public AppConfigurationClient(Uri endpoint, ConfigurationClient configurationClient, FeatureFlagClient featureFlagClient)
        {
            Endpoint = endpoint;
            _configurationClient = configurationClient;
            _featureFlagClient = featureFlagClient;
        }

        public Uri Endpoint { get; }

        public AsyncPageable<ConfigurationSetting> GetConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken)
        {
            return _configurationClient.GetConfigurationSettingsAsync(selector, cancellationToken);
        }

        public AsyncPageable<ConfigurationSetting> CheckConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken)
        {
            return _configurationClient.CheckConfigurationSettingsAsync(selector, cancellationToken);
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(string key, string label, CancellationToken cancellationToken)
        {
            return _configurationClient.GetConfigurationSettingAsync(key, label, cancellationToken);
        }

        public Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
        {
            return _configurationClient.GetConfigurationSettingAsync(setting, onlyIfChanged, cancellationToken);
        }

        public Task<Response<ConfigurationSnapshot>> GetSnapshotAsync(string snapshotName, CancellationToken cancellationToken)
        {
            return _configurationClient.GetSnapshotAsync(snapshotName, cancellationToken: cancellationToken);
        }

        public AsyncPageable<ConfigurationSetting> GetConfigurationSettingsForSnapshotAsync(string snapshotName, CancellationToken cancellationToken)
        {
            return _configurationClient.GetConfigurationSettingsForSnapshotAsync(snapshotName, cancellationToken);
        }

        public AsyncPageable<FeatureFlag> GetFeatureFlagsAsync(FeatureFlagSelector selector, CancellationToken cancellationToken)
        {
            return _featureFlagClient.GetFeatureFlagsAsync(selector, cancellationToken);
        }

        public void UpdateSyncToken(string syncToken)
        {
            _configurationClient.UpdateSyncToken(syncToken);
        }
    }
}
