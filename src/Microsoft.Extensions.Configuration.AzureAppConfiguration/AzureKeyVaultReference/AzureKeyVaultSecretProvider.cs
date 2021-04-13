// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
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
        private readonly IDictionary<string, SecretClient> _secretClients;
        private readonly TokenCredential _credential;
        private readonly Func<Uri, ValueTask<string>> _secretResolver;
        private readonly Dictionary<string, TimeSpan> _secretRefreshIntervals = new Dictionary<string, TimeSpan>();
        private HashSet<CachedKeyVaultSecret> _cachedKeyVaultSecrets = new HashSet<CachedKeyVaultSecret>();

        public AzureKeyVaultSecretProvider(TokenCredential credential = null, IEnumerable<SecretClient> secretClients = null, Func<Uri, ValueTask<string>> secretResolver = null, Dictionary<string, TimeSpan> secretRefreshIntervals = null)
        {
            _credential = credential;
            _secretClients = new Dictionary<string, SecretClient>(StringComparer.OrdinalIgnoreCase);
            _secretResolver = secretResolver;
            _secretRefreshIntervals = secretRefreshIntervals;

            if (secretClients != null)
            {
                foreach (SecretClient client in secretClients)
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

                // Try to load secret value from the cache first
                secretValue = GetCachedSecretValue(key);

                if (secretValue == null)
                {
                    // We dont have a cached secret value for this key vault reference.
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
                        RemoveExpiredSecretFromCache(key);
                    }
                }
            }
            else if (_secretResolver != null)
            {
                secretValue = await _secretResolver(secretUri).ConfigureAwait(false);
            }
            else
            {
                throw new UnauthorizedAccessException("No key vault credential or secret resolver callback configured, and no matching secret client could be found.");
            }

            return secretValue;
        }

        internal bool AnyExpiredSecrets()
        {
            bool shouldRefreshKeyVaultSecrets = false;
            List<CachedKeyVaultSecret> secretsToBeRemovedFromCache = new List<CachedKeyVaultSecret>();

            foreach (var cachedSecret in _cachedKeyVaultSecrets)
            {
                // Skip the refresh for this key vault secret if it has no expiration time or if it hasn't expired yet
                if (cachedSecret.ExpiresOn == null || DateTimeOffset.UtcNow < cachedSecret.ExpiresOn)
                {
                    continue;
                }

                // Remove the cached Key Vault secret for this key
                secretsToBeRemovedFromCache.Add(new CachedKeyVaultSecret(cachedSecret.Key));
                shouldRefreshKeyVaultSecrets = true;
            }

            secretsToBeRemovedFromCache.ForEach(secret => RemoveExpiredSecretFromCache(secret.Key));
            return shouldRefreshKeyVaultSecrets;
        }

        internal void RemoveAllSecretsFromCache()
        {
            _cachedKeyVaultSecrets.Clear();
        }

        internal void RemoveExpiredSecretFromCache(string key)
        {
            _cachedKeyVaultSecrets.Remove(new CachedKeyVaultSecret(key));
        }

        private SecretClient GetSecretClient(Uri secretUri)
        {
            string keyVaultId = secretUri.Host;

            if (_secretClients.TryGetValue(keyVaultId, out SecretClient client))
            {
                return client;
            }

            if (_credential == null)
            {
                return null;
            }

            client = new SecretClient(new Uri(secretUri.GetLeftPart(UriPartial.Authority)), _credential);
            _secretClients.Add(keyVaultId, client);
            return client;
        }

        private void UpdateCachedKeyVaultSecrets(string key, string secretValue)
        {
            DateTimeOffset? secretExpirationTime = null;

            if(_secretRefreshIntervals != null && _secretRefreshIntervals.TryGetValue(key, out TimeSpan refreshInterval))
            {
                // Set the cache expiration time using the refresh interval specified for this key
                secretExpirationTime = DateTimeOffset.UtcNow.Add(refreshInterval);
            }

            var cachedSecret = new CachedKeyVaultSecret(key);
            _cachedKeyVaultSecrets.Remove(cachedSecret);

            // If there is no refresh interval for this key, cache expiration time will be null,
            // i.e., this secret will not be refreshed automatically.
            cachedSecret.ExpiresOn = secretExpirationTime;
            cachedSecret.SecretValue = secretValue;
            _cachedKeyVaultSecrets.Add(cachedSecret);
        }

        private string GetCachedSecretValue(string key)
        {
            CachedKeyVaultSecret cachedSecret = _cachedKeyVaultSecrets.FirstOrDefault(secret => secret.Key == key);
            return cachedSecret?.SecretValue;
        }
    }
}
