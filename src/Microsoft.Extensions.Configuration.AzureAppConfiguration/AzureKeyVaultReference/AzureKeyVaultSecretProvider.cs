using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    class AzureKeyVaultSecretProvider
    {
        private readonly IDictionary<string, SecretClient> _secretClients;
        private readonly TokenCredential _defaultCredential;

        public AzureKeyVaultSecretProvider(TokenCredential defaultCredential = null, IEnumerable<SecretClient> secretClients = null)
        {
            _defaultCredential = defaultCredential;
            _secretClients = new Dictionary<string, SecretClient>();
            
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

            SecretClient client = GetSecretClient(secretUri);

            if (client == null)
            {
                // Unable to find a registered SecretClient or default credentials to fetch the value for the secret
                return null;
            }

            KeyVaultSecret secret = await client.GetSecretAsync(secretName, secretVersion, cancellationToken).ConfigureAwait(false);
            return secret?.Value;
        }

        private SecretClient GetSecretClient(Uri secretUri)
        {
            string keyVaultId = secretUri.Host;

            if (_secretClients.TryGetValue(keyVaultId, out SecretClient client))
            {
                return client;
            }

            if (_defaultCredential == null)
            {
                return null;
            }

            client = new SecretClient(new Uri(secretUri.GetLeftPart(UriPartial.Authority)), _defaultCredential);
            _secretClients.Add(keyVaultId, client);
            return client;
        }
    }
}
