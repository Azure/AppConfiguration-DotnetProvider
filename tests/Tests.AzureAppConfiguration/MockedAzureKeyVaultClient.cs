using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Rest.Azure;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.AzureAppConfiguration
{
    class MockedAzureKeyVaultClient : MockedAzureKeyVaultClientBase
    {
        private readonly string _secretValue;
        private readonly IEnumerable<KeyValuePair<string, string>> _keyValues;
        private readonly IKeyValue _kv;
        private readonly IEnumerable<IKeyValue> _kvCollectionPageOne;


        public bool IsEnabled { get; set; } = true;
        public bool HasAccessToKeyVault { get; set; } = true;

        public CancellationToken CancellationToken { get; set; }

        public IKeyValue kv { get; set; }
        public KeyVaultSecretReference secretRef { get; }



        public MockedAzureKeyVaultClient(string secretValue)
        {
            _secretValue = secretValue;
        }
        public MockedAzureKeyVaultClient(IKeyValue kv, string secretValue)
        {
            _secretValue = secretValue;
            _kv = kv;
        }

        public override Task<AzureOperationResponse<SecretBundle>> GetSecretWithHttpMessagesAsync(
            string vaultBaseUrl,
            string secretName,
            string secretVersion,
            Dictionary<string, List<string>> customHeaders = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsEnabled)
            {
                throw new KeyVaultErrorException()
                {
                    Body = new KeyVaultError(
                        new Error("Forbidden",
                        "Operation get is not allowed on a disabled secret.",
                        new Error()))
                };
            }

            if (!HasAccessToKeyVault)
            {
                throw new KeyVaultErrorException()
                {
                    Body = new KeyVaultError(
                        new Error("Forbidden",
                        "Access denied. Caller was not found on any access policy.",
                        new Error()))
                };
            }

            CancellationToken.ThrowIfCancellationRequested();

            var response = new AzureOperationResponse<SecretBundle>()
            {
                RequestId = Guid.NewGuid().ToString(),
                Body = new SecretBundle(_secretValue)
            };

            return Task.FromResult(response);
        }
    }
}
