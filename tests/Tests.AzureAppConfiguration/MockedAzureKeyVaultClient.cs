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
        public bool IsActive { get; set; } = true;
        public bool IsNotExpired { get; set; } = true;
        public bool HasAccessToKeyVault { get; set; } = true;

        public string messsage;

        public CancellationToken CancellationToken { get; set; }

        public IKeyValue kv { get; }
        public KeyVaultSecretReference secretRef { get; }
        public Exception inner { get; }



        public MockedAzureKeyVaultClient(IEnumerable<KeyValuePair<string, string>> keyValues)
        {
            _keyValues = keyValues;
        }

        public MockedAzureKeyVaultClient(string secretValue)
        {
            _secretValue = secretValue;
        }

        public MockedAzureKeyVaultClient(IKeyValue kv, IEnumerable<IKeyValue> kvCollectionPageOne)
        {
            _kv = kv;
            _kvCollectionPageOne = kvCollectionPageOne;
        }

        public override Task<AzureOperationResponse<SecretBundle>> GetSecretWithHttpMessagesAsync(
            string vaultBaseUrl,
            string secretName,
            string secretVersion,
            Dictionary<string, List<string>> customHeaders = null,
            CancellationToken cancellationToken = default)
        {
            if (IsEnabled == false)
            {
                throw new KeyVaultReferenceException("The Secret reference is not enabled", inner);
            }

            if (IsActive == false)
            {
                throw new KeyVaultReferenceException("The Secret reference is not active", inner);
            }

            if (IsNotExpired == false)
            {
                throw new KeyVaultReferenceException("The Secret reference is expired", inner);
            }

            if (HasAccessToKeyVault == false)
            {
                throw new KeyVaultReferenceException("You have no access to Key Vault", inner);
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
