// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    internal class AzureKeyVaultSecretProvider
    {
        private const string AzureIdentityAssemblyName = "Azure.Identity";

        private readonly AzureAppConfigurationKeyVaultOptions _keyVaultOptions;
        private readonly ConcurrentDictionary<string, SecretClient> _secretClients;
        private readonly ConcurrentDictionary<Uri, CachedKeyVaultSecret> _cachedKeyVaultSecrets;

        public bool IsParallelSecretResolutionEnabled => _keyVaultOptions.ParallelSecretResolutionEnabled;

        public AzureKeyVaultSecretProvider(AzureAppConfigurationKeyVaultOptions keyVaultOptions = null)
        {
            _keyVaultOptions = keyVaultOptions ?? new AzureAppConfigurationKeyVaultOptions();
            _cachedKeyVaultSecrets = new ConcurrentDictionary<Uri, CachedKeyVaultSecret>();
            _secretClients = new ConcurrentDictionary<string, SecretClient>(StringComparer.OrdinalIgnoreCase);

            if (_keyVaultOptions.SecretClients != null)
            {
                foreach (SecretClient client in _keyVaultOptions.SecretClients)
                {
                    string keyVaultId = client.VaultUri.Host;
                    _secretClients[keyVaultId] = client;
                }
            }
        }

        public async Task<string> GetSecretValue(KeyVaultSecretIdentifier secretIdentifier, ConfigurationSetting setting, string secretRefUri, Logger logger, CancellationToken cancellationToken)
        {
            string secretValue = null;

            if (_cachedKeyVaultSecrets.TryGetValue(secretIdentifier.SourceId, out CachedKeyVaultSecret cachedSecret) &&
                    (!cachedSecret.RefreshAt.HasValue || DateTimeOffset.UtcNow < cachedSecret.RefreshAt.Value))
            {
                return cachedSecret.SecretValue;
            }

            SecretClient client = GetSecretClient(secretIdentifier.SourceId);

            if (client == null && _keyVaultOptions.SecretResolver == null)
            {
                throw KeyVaultReferenceException.Create("No key vault credential or secret resolver callback configured, and no matching secret client could be found.", setting, null, secretRefUri);
            }

            CachedKeyVaultSecret updatedCachedSecret = null;
            bool success = false;

            try
            {
                if (client != null)
                {
                    KeyVaultSecret secret = await client.GetSecretAsync(secretIdentifier.Name, secretIdentifier.Version, cancellationToken).ConfigureAwait(false);
                    logger.LogDebug(LogHelper.BuildKeyVaultSecretReadMessage(setting.Key, setting.Label));
                    logger.LogInformation(LogHelper.BuildKeyVaultSettingUpdatedMessage(setting.Key));
                    secretValue = secret.Value;
                }
                else if (_keyVaultOptions.SecretResolver != null)
                {
                    secretValue = await _keyVaultOptions.SecretResolver(secretIdentifier.SourceId).ConfigureAwait(false);
                }

                updatedCachedSecret = new CachedKeyVaultSecret(secretValue, secretIdentifier.SourceId);
                success = true;
            }
            catch (Exception e) when (e is UnauthorizedAccessException || (e.Source?.Equals(AzureIdentityAssemblyName, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                throw KeyVaultReferenceException.Create(e.Message, setting, e, secretRefUri);
            }
            catch (Exception e) when (e is RequestFailedException || ((e as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false))
            {
                throw KeyVaultReferenceException.Create("Key vault error.", setting, e, secretRefUri);
            }
            finally
            {
                SetSecretInCache(secretIdentifier.SourceId, setting.Key, updatedCachedSecret, success);
            }

            return secretValue;
        }

        public bool ShouldRefreshKeyVaultSecrets()
        {
            foreach (KeyValuePair<Uri, CachedKeyVaultSecret> secret in _cachedKeyVaultSecrets)
            {
                if (secret.Value.RefreshAt.HasValue && secret.Value.RefreshAt.Value < DateTimeOffset.UtcNow)
                {
                    return true;
                }
            }

            return false;
        }

        public void ClearCache()
        {
            foreach (KeyValuePair<Uri, CachedKeyVaultSecret> secret in _cachedKeyVaultSecrets)
            {
                if (secret.Value.LastRefreshTime + RefreshConstants.MinimumSecretRefreshInterval < DateTimeOffset.UtcNow)
                {
                    _cachedKeyVaultSecrets.TryRemove(secret.Key, out _);
                }
            }
        }

        public void RemoveSecretFromCache(Uri sourceId)
        {
            _cachedKeyVaultSecrets.TryRemove(sourceId, out _);
        }

        private SecretClient GetSecretClient(Uri secretUri)
        {
            string keyVaultId = secretUri.Host;

            if (_secretClients.TryGetValue(keyVaultId, out SecretClient client))
            {
                return client;
            }

            if (_keyVaultOptions.Credential == null)
            {
                return null;
            }

            return _secretClients.GetOrAdd(
                keyVaultId,
                _ => new SecretClient(
                    new Uri(secretUri.GetLeftPart(UriPartial.Authority)),
                    _keyVaultOptions.Credential,
                    _keyVaultOptions.ClientOptions));
        }

        private void SetSecretInCache(Uri sourceId, string key, CachedKeyVaultSecret cachedSecret, bool success = true)
        {
            if (cachedSecret == null)
            {
                cachedSecret = new CachedKeyVaultSecret();
            }

            UpdateCacheExpirationTimeForSecret(key, cachedSecret, success);
            _cachedKeyVaultSecrets[sourceId] = cachedSecret;
        }

        private void UpdateCacheExpirationTimeForSecret(string key, CachedKeyVaultSecret cachedSecret, bool success)
        {
            if (!_keyVaultOptions.SecretRefreshIntervals.TryGetValue(key, out TimeSpan cacheExpirationTime))
            {
                if (_keyVaultOptions.DefaultSecretRefreshInterval.HasValue)
                {
                    cacheExpirationTime = _keyVaultOptions.DefaultSecretRefreshInterval.Value;
                }
            }

            if (cacheExpirationTime > TimeSpan.Zero)
            {
                if (success)
                {
                    cachedSecret.RefreshAttempts = 0;
                    cachedSecret.RefreshAt = DateTimeOffset.UtcNow.Add(cacheExpirationTime);
                }
                else
                {
                    if (cachedSecret.RefreshAttempts < int.MaxValue)
                    {
                        cachedSecret.RefreshAttempts++;
                    }

                    cachedSecret.RefreshAt = DateTimeOffset.UtcNow.Add(cacheExpirationTime.CalculateBackoffTime(RefreshConstants.DefaultMinBackoff, RefreshConstants.DefaultMaxBackoff, cachedSecret.RefreshAttempts));
                }
            }
        }
    }
}
