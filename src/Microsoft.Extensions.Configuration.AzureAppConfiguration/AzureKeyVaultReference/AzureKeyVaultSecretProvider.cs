﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Security.KeyVault.Secrets;
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
                secret = await client.GetSecretAsync(secretName, secretVersion, cancellationToken).ConfigureAwait(false);
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

        private void SetSecretInCache(string key, string secretValue)
        {
            DateTimeOffset? refreshSecretAt = null;

            if (_keyVaultOptions.SecretRefreshIntervals.TryGetValue(key, out TimeSpan refreshInterval))
            {
                refreshSecretAt = DateTimeOffset.UtcNow.Add(refreshInterval);
            }
            else if (_keyVaultOptions.DefaultSecretRefreshInterval.HasValue)
            {
                refreshSecretAt = DateTimeOffset.UtcNow.Add(_keyVaultOptions.DefaultSecretRefreshInterval.Value);
            }

            _cachedKeyVaultSecrets[key] = new CachedKeyVaultSecret(secretValue, refreshSecretAt);
            
            if (key == _nextRefreshKey)
            {
                UpdateNextRefreshableSecretFromCache();
            }
            else if ((refreshSecretAt.HasValue && _nextRefreshTime.HasValue && refreshSecretAt.Value < _nextRefreshTime.Value)
                    || (refreshSecretAt.HasValue && !_nextRefreshTime.HasValue))
            {
                _nextRefreshKey = key;
                _nextRefreshTime = refreshSecretAt.Value;
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
    }
}
