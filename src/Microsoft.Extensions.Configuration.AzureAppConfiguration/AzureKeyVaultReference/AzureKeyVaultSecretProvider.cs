// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Logging;
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

        public async Task<string> GetSecretValue(Uri secretUri, string key, ILogger logger, CancellationToken cancellationToken)
        {
            string secretName = secretUri?.Segments?.ElementAtOrDefault(2)?.TrimEnd('/');
            string secretVersion = secretUri?.Segments?.ElementAtOrDefault(3)?.TrimEnd('/');
            string secretValue = null;

            if (_cachedKeyVaultSecrets.TryGetValue(key, out CachedKeyVaultSecret cachedSecret) &&
                    (!cachedSecret.RefreshAt.HasValue || DateTimeOffset.UtcNow < cachedSecret.RefreshAt.Value))
            {
                return cachedSecret.SecretValue;
            }

            SecretClient client = GetSecretClient(secretUri);

            if (client == null && _keyVaultOptions.SecretResolver == null)
            {
                throw new UnauthorizedAccessException("No key vault credential or secret resolver callback configured, and no matching secret client could be found.");
            }

            bool success = false;

            try
            {
                if (client != null)
                {
                    KeyVaultSecret secret = await client.GetSecretAsync(secretName, secretVersion, cancellationToken).ConfigureAwait(false);
                    logger?.LogInformation(LoggingConstants.RefreshKeyVaultSecretUpdatedSuccess + client.VaultUri);
                    logger?.LogDebug(LoggingConstants.RefreshKeyVaultSecretChanged + secret.Name);
                    secretValue = secret.Value;
                }
                else if (_keyVaultOptions.SecretResolver != null)
                {
                    secretValue = await _keyVaultOptions.SecretResolver(secretUri).ConfigureAwait(false);
                }

                cachedSecret = new CachedKeyVaultSecret(secretValue);
                success = true;
            }
            finally
            {
                SetSecretInCache(key, cachedSecret, success);
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

        private void SetSecretInCache(string key, CachedKeyVaultSecret cachedSecret, bool success = true)
        {
            if (cachedSecret == null)
            {
                cachedSecret = new CachedKeyVaultSecret();
            }

            UpdateCacheExpirationTimeForSecret(key, cachedSecret, success);
            _cachedKeyVaultSecrets[key] = cachedSecret;

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
