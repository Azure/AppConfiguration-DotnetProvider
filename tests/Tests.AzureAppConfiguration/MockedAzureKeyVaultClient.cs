using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.AppConfiguration.Azconfig;
using Xunit.Sdk;
using System.Net.Http;
using System.Net;
using Newtonsoft.Json;
using System.Text;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Rest.Azure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

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

        public override Task<AzureOperationResponse<SecretBundle>> GetSecretWithHttpMessagesAsync(string vaultBaseUrl, string secretName, string secretVersion, Dictionary<string, List<string>> customHeaders = null, CancellationToken cancellationToken = default)
        {
            if (IsEnabled == false)
            {
                throw new KeyVaultErrorException();
            }

            if (IsActive == false)
            {
                throw new KeyVaultErrorException();
            }

            if (IsNotExpired == false)
            {
                throw new KeyVaultErrorException();
            }
            var response = new AzureOperationResponse<SecretBundle>()
            {
                RequestId = Guid.NewGuid().ToString(),
                Body = new SecretBundle(_secretValue)
            };

            return Task.FromResult(response);
        }
    }
}
