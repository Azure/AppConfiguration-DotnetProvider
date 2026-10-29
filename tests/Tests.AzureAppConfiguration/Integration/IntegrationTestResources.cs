// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    internal class IntegrationTestResources
    {
        internal const string TestKeyPrefix = "IntegrationTest";

        private readonly ConfigurationClient _configurationClient;
        private readonly SecretClient _secretClient;
        private readonly HashSet<(string Key, string Label)> _settings = new HashSet<(string Key, string Label)>();
        private readonly HashSet<string> _snapshots = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _secrets = new HashSet<string>(StringComparer.Ordinal);

        public IntegrationTestResources(ConfigurationClient configurationClient, SecretClient secretClient)
        {
            if (configurationClient == null)
            {
                throw new ArgumentNullException(nameof(configurationClient));
            }

            if (secretClient == null)
            {
                throw new ArgumentNullException(nameof(secretClient));
            }

            _configurationClient = configurationClient;
            _secretClient = secretClient;
        }

        public Task<Response<ConfigurationSetting>> SetConfigurationSettingAsync(
            ConfigurationSetting setting, CancellationToken cancellationToken = default)
        {
            if (setting == null)
            {
                throw new ArgumentNullException(nameof(setting));
            }

            // Track before sending: a failed response does not guarantee that the service rejected the write.
            _settings.Add((setting.Key, setting.Label));
            return _configurationClient.SetConfigurationSettingAsync(setting, cancellationToken: cancellationToken);
        }

        public Task<Response<KeyVaultSecret>> SetSecretAsync(
            string name, string value, CancellationToken cancellationToken = default)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            _secrets.Add(name);
            return _secretClient.SetSecretAsync(name, value, cancellationToken);
        }

        public Task<CreateSnapshotOperation> CreateSnapshotAsync(
            string name, ConfigurationSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            _snapshots.Add(name);
            return _configurationClient.CreateSnapshotAsync(WaitUntil.Completed, name, snapshot, cancellationToken);
        }

        public Task CleanupAsync(Action<string> output, CancellationToken cancellationToken = default)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            return CleanupAsync(new List<RequestFailedException>(), output, cancellationToken);
        }

        public async Task CleanupStaleResourcesAsync(
            DateTimeOffset cutoffTime, Action<string> output, CancellationToken cancellationToken = default)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            var failures = new List<RequestFailedException>();

            foreach (string prefix in new[] { TestKeyPrefix + "-", FeatureManagementConstants.FeatureFlagMarker + TestKeyPrefix + "-" })
            {
                await TryCleanupAsync(async token =>
                {
                    await foreach (ConfigurationSetting setting in _configurationClient.GetConfigurationSettingsAsync(
                        new SettingSelector { KeyFilter = prefix + "*" }, token))
                    {
                        if (setting.LastModified < cutoffTime)
                        {
                            _settings.Add((setting.Key, setting.Label));
                        }
                    }
                }, $"list settings with prefix '{prefix}'", failures, output, false, cancellationToken);
            }

            await TryCleanupAsync(async token =>
            {
                var selector = new SnapshotSelector
                {
                    Status = { ConfigurationSnapshotStatus.Ready }
                };

                await foreach (ConfigurationSnapshot snapshot in _configurationClient.GetSnapshotsAsync(selector, token))
                {
                    if (IsTestSnapshot(snapshot.Name) &&
                        snapshot.CreatedOn < cutoffTime &&
                        snapshot.Status == ConfigurationSnapshotStatus.Ready)
                    {
                        _snapshots.Add(snapshot.Name);
                    }
                }
            }, "list snapshots", failures, output, false, cancellationToken);

            await TryCleanupAsync(async token =>
            {
                await foreach (SecretProperties secret in _secretClient.GetPropertiesOfSecretsAsync(token))
                {
                    if (secret.Name.StartsWith(TestKeyPrefix + "-", StringComparison.Ordinal) &&
                        secret.CreatedOn < cutoffTime)
                    {
                        _secrets.Add(secret.Name);
                    }
                }
            }, "list secrets", failures, output, false, cancellationToken);

            await CleanupAsync(failures, output, cancellationToken);
        }

        private async Task CleanupAsync(
            List<RequestFailedException> failures, Action<string> output, CancellationToken cancellationToken)
        {
            int settingCount = _settings.Count;
            int snapshotCount = _snapshots.Count;
            int secretCount = _secrets.Count;

            foreach ((string Key, string Label) setting in _settings.ToArray())
            {
                if (await TryCleanupAsync(
                    token => _configurationClient.DeleteConfigurationSettingAsync(setting.Key, setting.Label, token),
                    $"delete setting '{setting.Key}' (label '{setting.Label}')", failures, output, true, cancellationToken))
                {
                    _settings.Remove(setting);
                }
            }

            foreach (string snapshot in _snapshots.ToArray())
            {
                if (await TryCleanupAsync(
                    token => _configurationClient.ArchiveSnapshotAsync(snapshot, token),
                    $"archive snapshot '{snapshot}'", failures, output, true, cancellationToken))
                {
                    _snapshots.Remove(snapshot);
                }
            }

            foreach (string secret in _secrets.ToArray())
            {
                if (await TryCleanupAsync(
                    token => DeleteSecretAsync(secret, output, token),
                    $"delete secret '{secret}'", failures, output, true, cancellationToken))
                {
                    _secrets.Remove(secret);
                }
            }

            output($"Resource cleanup completed: {settingCount - _settings.Count} settings/feature flags, " +
                $"{snapshotCount - _snapshots.Count} snapshots, {secretCount - _secrets.Count} secrets; " +
                $"{_settings.Count + _snapshots.Count + _secrets.Count} resources remaining.");

            if (failures.Count > 0)
            {
                throw new AggregateException("Integration test resource cleanup failed.", failures);
            }
        }

        private async Task DeleteSecretAsync(string name, Action<string> output, CancellationToken cancellationToken)
        {
            const int maxAttempts = 4;
            DeleteSecretOperation operation;

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    operation = await _secretClient.StartDeleteSecretAsync(name, cancellationToken);
                    break;
                }
                catch (RequestFailedException ex) when (ex.Status == 409 && attempt < maxAttempts)
                {
                    // Framework runs can discover and delete the same stale secret concurrently.
                    output($"Secret deletion conflict for '{name}'; retrying in {attempt}s (attempt {attempt + 1}/{maxAttempts}).");
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
            }

            await operation.WaitForCompletionAsync(cancellationToken);
        }

        private static async Task<bool> TryCleanupAsync(
            Func<CancellationToken, Task> operation,
            string description,
            List<RequestFailedException> failures,
            Action<string> output,
            bool ignoreNotFound,
            CancellationToken cancellationToken)
        {
            try
            {
                await operation(cancellationToken);
                return true;
            }
            catch (RequestFailedException ex) when (ignoreNotFound && ex.Status == 404)
            {
                output($"Resource already absent: {description}.");
                return true;
            }
            catch (RequestFailedException ex)
            {
                output($"Failed to {description}: {ex.Message}");
                failures.Add(ex);
                return false;
            }
        }

        private static bool IsTestSnapshot(string name)
        {
            return name.StartsWith("snapshot-" + TestKeyPrefix + "-", StringComparison.Ordinal) ||
                name.StartsWith("snapshot-key-" + TestKeyPrefix + "-", StringComparison.Ordinal) ||
                name.StartsWith("snapshot-invalid-" + TestKeyPrefix + "-", StringComparison.Ordinal) ||
                name.StartsWith("snapshot-ff-" + TestKeyPrefix + "-", StringComparison.Ordinal) ||
                (name.StartsWith(TestKeyPrefix + "-", StringComparison.Ordinal) &&
                    name.EndsWith("-snapshot", StringComparison.Ordinal));
        }
    }
}
