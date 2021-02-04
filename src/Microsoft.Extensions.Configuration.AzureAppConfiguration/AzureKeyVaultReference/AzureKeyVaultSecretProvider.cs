// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core;
using Azure.Data.AppConfiguration;
using Azure.Security.KeyVault.Certificates;
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
        private const string AzureIdentityAssemblyName = "Azure.Identity";
        private readonly IDictionary<string, SecretClient> _secretClients;
        private readonly IDictionary<string, CertificateClient> _certificateClients;
        private readonly TokenCredential _credential;
        private readonly Func<Uri, ValueTask<string>> _secretResolver;
        private HashSet<CachedKeyVaultSecret> _cachedKeyVaultSecrets = new HashSet<CachedKeyVaultSecret>();

        public AzureKeyVaultSecretProvider(TokenCredential credential = null, IEnumerable<SecretClient> secretClients = null, IEnumerable<CertificateClient> certificateClients = null, Func<Uri, ValueTask<string>> secretResolver = null)
        {
            _credential = credential;
            _secretClients = new Dictionary<string, SecretClient>(StringComparer.OrdinalIgnoreCase);
            _certificateClients = new Dictionary<string, CertificateClient>(StringComparer.OrdinalIgnoreCase);
            _secretResolver = secretResolver;

            if (secretClients != null)
            {
                foreach (SecretClient client in secretClients)
                {
                    string keyVaultId = client.VaultUri.Host;
                    _secretClients[keyVaultId] = client;
                }
            }

            if (certificateClients != null)
            {
                foreach (CertificateClient client in certificateClients)
                {
                    string keyVaultId = client.VaultUri.Host;
                    _certificateClients[keyVaultId] = client;
                }
            }
        }

        public async Task<string> GetSecretValue(Uri secretUri, ConfigurationSetting setting, CancellationToken cancellationToken)
        {
            string secretName = secretUri?.Segments?.ElementAtOrDefault(2)?.TrimEnd('/');
            string secretVersion = secretUri?.Segments?.ElementAtOrDefault(3)?.TrimEnd('/');
            string secretValue;

            SecretClient client = GetSecretClient(secretUri);

            if (client != null)
            {
                // Try to load secret value from the cache first
                secretValue = GetCachedSecretValue(setting.Key, setting.Label);
                
                if (secretValue == null)
                {
                    KeyVaultSecret secret;

                    try
                    {
                        secret = await client.GetSecretAsync(secretName, secretVersion, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (
                        e is UnauthorizedAccessException ||
                        (e.Source?.Equals(AzureIdentityAssemblyName, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        e is RequestFailedException ||
                        ((e as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false))
                    {
                        // Permission to get secrets may have been revoked.
                        // Delete any cached secret for this key and label and rethrow exception.
                        RemoveExpiredSecretFromCache(setting.Key, setting.Label);
                        throw;
                    }

                    secretValue = secret?.Value;

                    if (secret != null)
                    {
                        UpdateCachedKeyVaultSecrets(secret, setting.Key, setting.Label, cancellationToken);
                    }
                    else
                    {
                        // Secret may have been deleted from KeyVault.
                        // Delete the secret from cache too.
                        RemoveExpiredSecretFromCache(setting.Key, setting.Label);
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

                // Remove the cached Key Vault secret for this key and label
                secretsToBeRemovedFromCache.Add(new CachedKeyVaultSecret(cachedSecret.Key, cachedSecret.Label));
                shouldRefreshKeyVaultSecrets = true;
            }

            secretsToBeRemovedFromCache.ForEach(secret => RemoveExpiredSecretFromCache(secret.Key, secret.Label));
            return shouldRefreshKeyVaultSecrets;
        }

        internal void RemoveAllSecretsFromCache()
        {
            _cachedKeyVaultSecrets.Clear();
        }

        internal void RemoveExpiredSecretFromCache(string key, string label)
        {
            _cachedKeyVaultSecrets.Remove(new CachedKeyVaultSecret(key, label));
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

        private CertificateClient GetCertificateClient(Uri secretUri)
        {
            string keyVaultId = secretUri.Host;

            if (_certificateClients.TryGetValue(keyVaultId, out CertificateClient client))
            {
                return client;
            }

            if (_credential == null)
            {
                return null;
            }

            client = new CertificateClient(new Uri(secretUri.GetLeftPart(UriPartial.Authority)), _credential);
            _certificateClients.Add(keyVaultId, client);
            return client;
        }

        private async Task<DateTimeOffset?> GetSecretExpirationTime(KeyVaultSecret secret, CancellationToken cancellationToken)
        {
            DateTimeOffset? secretExpirationTime = secret.Properties.ExpiresOn;

            if (secret.Properties.Managed)
            {
                CertificateClient certClient = GetCertificateClient(secret.Id);

                if (certClient != null)
                {
                    KeyVaultCertificateWithPolicy latestCertificate = null;

                    try
                    {
                        latestCertificate = await certClient.GetCertificateAsync(secret.Name, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e) when (
                        e is UnauthorizedAccessException ||
                        (e.Source?.Equals(AzureIdentityAssemblyName, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        e is RequestFailedException ||
                        ((e as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false))
                    {
                        // User may not have the right permissions to get certificates, but have the permission to get secrets.
                        // Use the expiry date of the secret, if available. Otherwise, treat this as a non-rotating secret.
                    }

                    // Calculate the auto-renewal time only if this is the latest version of certificate.
                    // If the secret reference is for an older version of certificate, CertificatePolicy
                    // does not apply to this certificate because it will never be auto-rotated.
                    if (latestCertificate != null && latestCertificate.Properties != null && latestCertificate.Properties.Version == secret.Properties.Version)
                    {
                        secretExpirationTime = latestCertificate.Properties.ExpiresOn;
                        CertificatePolicy policy = latestCertificate?.Policy;

                        if (policy?.LifetimeActions != null)
                        {
                            // Currently, only a single LifetimeAction is allowed. It will either be "AutoRenew" or "EmailContacts".
                            var autoRenewPolicy = policy.LifetimeActions.FirstOrDefault(act => act.Action == CertificatePolicyAction.AutoRenew);

                            if (autoRenewPolicy != null)
                            {
                                // Either DaysBeforeExpiry or LifetimePercentage will be present
                                if (autoRenewPolicy.DaysBeforeExpiry.HasValue)
                                {
                                    int daysBeforeExpiry = autoRenewPolicy.DaysBeforeExpiry.Value;
                                    secretExpirationTime = (DateTimeOffset)(latestCertificate.Properties.ExpiresOn?.AddDays(-daysBeforeExpiry));
                                }
                                else if (autoRenewPolicy.LifetimePercentage.HasValue)
                                {
                                    int lifetimePercentage = autoRenewPolicy.LifetimePercentage.Value;
                                    var startTime = (DateTimeOffset)latestCertificate.Properties.CreatedOn;
                                    var endTime = (DateTimeOffset)latestCertificate.Properties.ExpiresOn;
                                    var diff = (endTime - startTime).Ticks;
                                    var certLifetimeTicks = diff * lifetimePercentage / 100;
                                    secretExpirationTime = startTime.AddTicks(certLifetimeTicks);
                                }
                            }
                        }
                    }
                }
            }
                
            return secretExpirationTime;
        }

        private async void UpdateCachedKeyVaultSecrets(KeyVaultSecret secret, string key, string label, CancellationToken cancellationToken)
        {
            DateTimeOffset? secretExpirationTime = await GetSecretExpirationTime(secret, cancellationToken).ConfigureAwait(false);
            
            var cachedSecret = new CachedKeyVaultSecret(key, label);
            _cachedKeyVaultSecrets.Remove(cachedSecret);

            // Users may be referencing secrets that have already expired in Key Vault. Cache the secret only if it's not already expired.
            // No need to cache expired secrets since they will be fetched from Key Vault with every RefreshAsync call.
            if (secretExpirationTime == null || DateTime.UtcNow < secretExpirationTime)
            {
                cachedSecret.ExpiresOn = secretExpirationTime;
                cachedSecret.SecretValue = secret.Value;
                _cachedKeyVaultSecrets.Add(cachedSecret);
            }
        }

        private string GetCachedSecretValue(string key, string label)
        {
            CachedKeyVaultSecret cachedSecret = _cachedKeyVaultSecrets.FirstOrDefault(secret => secret.Key == key && secret.Label == label);
            string cachedSecretValue = null;

            if(cachedSecret != null && (cachedSecret.ExpiresOn == null || DateTimeOffset.UtcNow < cachedSecret.ExpiresOn))
            {
                // Return cached secret if it has no expiration time or if it hasn't expired yet
                cachedSecretValue = cachedSecret.SecretValue;
            }

            return cachedSecretValue;
        }
    }
}
