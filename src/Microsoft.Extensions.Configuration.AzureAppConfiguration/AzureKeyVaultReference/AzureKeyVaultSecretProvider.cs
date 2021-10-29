// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    internal class AzureKeyVaultSecretProvider
    {
        private readonly AzureAppConfigurationKeyVaultOptions _keyVaultOptions;
        private readonly IDictionary<string, SecretClient> _secretClients;
        private readonly Dictionary<string, CachedKeyVaultSecret> _cachedKeyVaultSecrets;
        private string _nextRefreshKey;
        private DateTimeOffset? _nextRefreshTime;

        public AzureKeyVaultSecretProvider(AzureAppConfigurationKeyVaultOptions keyVaultOptions = null)
        {
            _keyVaultOptions = keyVaultOptions ?? new AzureAppConfigurationKeyVaultOptions();
            _cachedKeyVaultSecrets = new Dictionary<string, CachedKeyVaultSecret>(StringComparer.OrdinalIgnoreCase);
            _secretClients = new Dictionary<string, SecretClient>(StringComparer.OrdinalIgnoreCase);

            if (_keyVaultOptions.SecretClients != null)
            {
                foreach (SecretClient client in _keyVaultOptions.SecretClients)
                {
                    string keyVaultId = client.VaultUri.Host;
                    _secretClients[keyVaultId] = client;
                }
            }
        }

        public async Task<string> GetSecretValue(Uri secretUri, string key, CancellationToken cancellationToken)
        {
            string secretName = secretUri?.Segments?.ElementAtOrDefault(2)?.TrimEnd('/');
            string secretVersion = secretUri?.Segments?.ElementAtOrDefault(3)?.TrimEnd('/');
            string secretValue;

            SecretClient client = GetSecretClient(secretUri);

            if (_cachedKeyVaultSecrets.TryGetValue(key, out CachedKeyVaultSecret cachedSecret) &&
                    (!cachedSecret.RefreshAt.HasValue || DateTimeOffset.UtcNow < cachedSecret.RefreshAt.Value))
            {
                secretValue = cachedSecret.SecretValue;
            }
            else if (client != null)
            {
                KeyVaultSecret secret;
                bool success = false;

                try
                {
                    secret = await client.GetSecretAsync(secretName, secretVersion, cancellationToken).ConfigureAwait(false);
                    success = true;
                }
                finally
                {
                    // If this is not already a cached secret, we don't have any refresh time to update,
                    // i.e. there's no way to track next refresh time of new secrets that fail to resolve.
                    if (!success && cachedSecret != null)
                    {
                        SetSecretInCache(key, secretValue: null, cachedSecret: cachedSecret, success: false);
                    }
                }

                secretValue = secret?.Value;
                SetSecretInCache(key, secretValue);
            }
            else if (_keyVaultOptions.SecretResolver != null)
            {
                secretValue = await _keyVaultOptions.SecretResolver(secretUri).ConfigureAwait(false);
                SetSecretInCache(key, secretValue);
            }
            else
            {
                throw new UnauthorizedAccessException("No key vault credential or secret resolver callback configured, and no matching secret client could be found.");
            }

            return secretValue;
        }

        public bool ShouldRefreshKeyVaultSecrets()
        {
            return _nextRefreshTime.HasValue && _nextRefreshTime.Value < DateTimeOffset.UtcNow;
        }

        public void ClearCache()
        {
            _cachedKeyVaultSecrets.Clear();
            _nextRefreshKey = null;
            _nextRefreshTime = null;
        }

        public void RemoveSecretFromCache(string key)
        {
            _cachedKeyVaultSecrets.Remove(key);

            if (key == _nextRefreshKey)
            {
                UpdateNextRefreshableSecretFromCache();
            }
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

            client = new SecretClient(new Uri(secretUri.GetLeftPart(UriPartial.Authority)), _keyVaultOptions.Credential);
            _secretClients.Add(keyVaultId, client);
            return client;
        }

        private void SetSecretInCache(string key, string secretValue, CachedKeyVaultSecret cachedSecret = null, bool success = true)
        {
            if (success)
            {
                cachedSecret = new CachedKeyVaultSecret(secretValue);
                _cachedKeyVaultSecrets[key] = cachedSecret;
            }

            cachedSecret.RefreshAt = GetCacheExpirationTimeForSecret(key, cachedSecret, success);

            if (key == _nextRefreshKey)
            {
                UpdateNextRefreshableSecretFromCache();
            }
            else if ((cachedSecret.RefreshAt.HasValue && _nextRefreshTime.HasValue && cachedSecret.RefreshAt.Value < _nextRefreshTime.Value)
                    || (cachedSecret.RefreshAt.HasValue && !_nextRefreshTime.HasValue))
            {
                _nextRefreshKey = key;
                _nextRefreshTime = cachedSecret.RefreshAt.Value;
            }
        }

        private void UpdateNextRefreshableSecretFromCache()
        {
            _nextRefreshKey = null;
            _nextRefreshTime = DateTimeOffset.MaxValue;

            foreach (KeyValuePair<string, CachedKeyVaultSecret> secret in _cachedKeyVaultSecrets)
            {
                if (secret.Value.RefreshAt.HasValue && secret.Value.RefreshAt.Value < _nextRefreshTime)
                {
                    _nextRefreshTime = secret.Value.RefreshAt;
                    _nextRefreshKey = secret.Key;
                }
            }

            if (_nextRefreshTime == DateTimeOffset.MaxValue)
            {
                _nextRefreshTime = null;
            }
        }

        private DateTimeOffset? GetCacheExpirationTimeForSecret(string key, CachedKeyVaultSecret cachedSecret, bool success)
        {
            DateTimeOffset? refreshSecretAt = null;

            if (success)
            {
                cachedSecret.RefreshAttempts = 0;

                if (_keyVaultOptions.SecretRefreshIntervals.TryGetValue(key, out TimeSpan refreshInterval))
                {
                    refreshSecretAt = DateTimeOffset.UtcNow.Add(refreshInterval);
                }
                else if (_keyVaultOptions.DefaultSecretRefreshInterval.HasValue)
                {
                    refreshSecretAt = DateTimeOffset.UtcNow.Add(_keyVaultOptions.DefaultSecretRefreshInterval.Value);
                }
            }
            else
            {
                if (cachedSecret.RefreshAttempts < int.MaxValue)
                {
                    cachedSecret.RefreshAttempts++;
                }

                if (_keyVaultOptions.SecretRefreshIntervals.TryGetValue(key, out TimeSpan refreshInterval))
                {
                    refreshSecretAt = DateTimeOffset.UtcNow.Add(refreshInterval.CalculateBackoffTime(cachedSecret.RefreshAttempts));
                }
                else if (_keyVaultOptions.DefaultSecretRefreshInterval.HasValue)
                {
                    refreshSecretAt = DateTimeOffset.UtcNow.Add(_keyVaultOptions.DefaultSecretRefreshInterval.Value.CalculateBackoffTime(cachedSecret.RefreshAttempts));
                }
            }

            return refreshSecretAt;
        }
    }
}
