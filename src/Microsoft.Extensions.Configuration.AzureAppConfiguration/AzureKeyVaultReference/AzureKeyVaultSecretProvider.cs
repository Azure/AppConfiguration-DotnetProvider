// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Security.KeyVault.Secrets;
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
        private readonly IDictionary<string, SecretClient> _secretClients;
        private ConcurrentDictionary<string, CachedKeyVaultSecret> _cachedKeyVaultSecrets = new ConcurrentDictionary<string, CachedKeyVaultSecret>();
        private AzureAppConfigurationKeyVaultOptions _keyVaultOptions;

        public AzureKeyVaultSecretProvider(AzureAppConfigurationKeyVaultOptions keyVaultOptions = null)
        {
            _keyVaultOptions = keyVaultOptions ?? new AzureAppConfigurationKeyVaultOptions();
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

            if (client != null)
            {
                KeyVaultSecret secret;

                // Try to load secret value from the cache first.
                secretValue = GetCachedSecretValue(key);

                if (secretValue == null)
                {
                    // We dont have a cached secret value or the cached value has expired.
                    // Get the secret from Key Vault and update the cache.
                    secret = await client.GetSecretAsync(secretName, secretVersion, cancellationToken).ConfigureAwait(false);
                    secretValue = secret?.Value;

                    if (secret != null)
                    {
                        UpdateCachedKeyVaultSecrets(key, secretValue);
                    }
                    else
                    {
                        // Secret may have been deleted from KeyVault.
                        // Delete the secret from cache too.
                        RemoveSecretFromCache(key);
                    }
                }
            }
            else if (_keyVaultOptions.SecretResolver != null)
            {
                secretValue = await _keyVaultOptions.SecretResolver(secretUri).ConfigureAwait(false);
            }
            else
            {
                throw new UnauthorizedAccessException("No key vault credential or secret resolver callback configured, and no matching secret client could be found.");
            }

            return secretValue;
        }

        public bool ShouldRefreshKeyVaultSecrets()
        {
            // return true if the RefreshAt time of any cached secret has already elapsed.
            return _cachedKeyVaultSecrets.Any(cachedSecret => cachedSecret.Value.RefreshAt.HasValue && cachedSecret.Value.RefreshAt.Value < DateTimeOffset.UtcNow);
        }

        public void ClearCache()
        {
            _cachedKeyVaultSecrets.Clear();
        }

        public void RemoveSecretFromCache(string key)
        {
            _cachedKeyVaultSecrets.TryRemove(key, out CachedKeyVaultSecret _);
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

        private void UpdateCachedKeyVaultSecrets(string key, string secretValue)
        {
            // If refresh interval for this key or a default refresh interval for all keys has not been specified,
            // cache expiration time will be null, i.e., this secret will not be refreshed automatically.
            DateTimeOffset? refreshSecretAt = null;

            if (_keyVaultOptions.SecretRefreshIntervals.TryGetValue(key, out TimeSpan refreshInterval))
            {
                // Set the cache expiration time using the refresh interval specified for this key.
                refreshSecretAt = DateTimeOffset.UtcNow.Add(refreshInterval);
            }
            else if (_keyVaultOptions.DefaultSecretRefreshInterval.HasValue)
            {
                // Set the cache expiration time using the default refresh interval specified for all keys.
                refreshSecretAt = DateTimeOffset.UtcNow.Add(_keyVaultOptions.DefaultSecretRefreshInterval.Value);
            }

            // Add or update the cache.
            _cachedKeyVaultSecrets[key] = new CachedKeyVaultSecret(secretValue, refreshSecretAt);
        }

        private string GetCachedSecretValue(string key)
        {
            string cachedSecretValue = null;

            // Use the cached value of this key vault secret if RefreshAt time is null or in the future
            if (_cachedKeyVaultSecrets.TryGetValue(key, out CachedKeyVaultSecret cachedSecret) && 
                (!cachedSecret.RefreshAt.HasValue || DateTimeOffset.UtcNow < cachedSecret.RefreshAt.Value))
            {
                cachedSecretValue = cachedSecret.SecretValue;
            }

            return cachedSecretValue;
        }
    }
}
