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

        public AzureKeyVaultSecretProvider(TokenCredential credential = null, IEnumerable<SecretClient> secretClients = null, Func<Uri, ValueTask<string>> secretResolver = null)
        {
            _credential = credential;
            _secretClients = new Dictionary<string, SecretClient>(StringComparer.OrdinalIgnoreCase);
            _secretResolver = secretResolver;

            if (secretClients != null)
            {
                foreach (SecretClient client in secretClients)
                {
                    string keyVaultId = client.VaultUri.Host;
                    _secretClients[keyVaultId] = client;
                }
            }
        }

        public async Task<string> GetSecretValue(Uri secretUri, CancellationToken cancellationToken)
        {
            if (secretUri == null)
            {
                throw new ArgumentNullException(nameof(secretUri));
            }

            string secretName = secretUri?.Segments?.ElementAtOrDefault(2)?.TrimEnd('/');
            string secretVersion = secretUri?.Segments?.ElementAtOrDefault(3)?.TrimEnd('/');
            string secretValue;

            SecretClient client = GetSecretClient(secretUri);

            if (client != null)
            {
                KeyVaultSecret secret = await client.GetSecretAsync(secretName, secretVersion, cancellationToken).ConfigureAwait(false);
                secretValue = secret?.Value;
            }
            else if (_secretResolver != null)
            {
                secretValue = await _secretResolver(secretUri);
            }
            else
            {
                throw new UnauthorizedAccessException("No key vault credential or secret resolver callback configured, and no matching secret client could be found.");
            }

            return secretValue;
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
    }
}
