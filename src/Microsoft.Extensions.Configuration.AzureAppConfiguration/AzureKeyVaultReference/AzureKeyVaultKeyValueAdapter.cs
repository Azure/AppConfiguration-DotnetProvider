using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Newtonsoft.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    internal class AzureKeyVaultKeyValueAdapter : IKeyValueAdapter
    {
        public AzureKeyVaultKeyValueAdapter()
        {

        }

        private static readonly JsonSerializerSettings s_SerializationSettings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
        private IKeyVaultClient _keyVaultClient;

        /// <summary> Uses the managed identity to retrieve the actual value </summary>
        /// <param KeyValue ="IKeyValue">  inputs the IKeyValue </param>
        /// returns the keyname and actual value
        public async Task<IEnumerable<KeyValuePair<string, string>>> GetKeyValues(IKeyValue keyValue)
        {
            string contentType = keyValue?.ContentType?.Split(';')[0].Trim();

            //Checking if the content type is our type (If not we return null)
            if (!string.Equals(contentType, KeyVaultConstants.ContentType))
            {
                return null;
            }

            KeyVaultSecretReference secretRef = JsonConvert.DeserializeObject<KeyVaultSecretReference>(keyValue.Value, s_SerializationSettings);

            //Get secret from KeyVault
            string secret = await GetSecretFromKeyVault(secretRef.Uri);

            var keyValues = new List<KeyValuePair<string, string>>();

            // add the key and it's value in the keyvaluePair
            keyValues.Add(new KeyValuePair<string, string>(keyValue.Key, secret));

            return keyValues;
        }

        /// <summary>
        ///  Uses the managed identity to retrieve the actual value
        /// </summary>
        /// <param SecretUri ="s">  inputs the reference(uri) of the key </param>
        /// Returns actual value
        internal async Task<string> GetSecretFromKeyVault(string secretUri, Func<Task<SecretBundle>> f = null)
        {
            if (f == null)
            { 
                if (_keyVaultClient == null)
                {
                    //Use Managed identity
                    var azureServiceTokenProvider = new AzureServiceTokenProvider();

                    _keyVaultClient =
                    new KeyVaultClient(
                        new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
                }

                f = () => _keyVaultClient.GetSecretAsync(secretUri);
            }

            SecretBundle secret = await GetSecret(f);

            return secret?.Value;
        }

        private async Task<SecretBundle> GetSecret(Func<Task<SecretBundle>> f)
        {
                return await f();
        }
    }
}
