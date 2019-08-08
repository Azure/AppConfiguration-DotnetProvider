using Microsoft.Azure.AppConfiguration.Azconfig;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    class AzureKeyVaultKeyValueAdapter : IKeyValueAdapter
    {
        private static readonly JsonSerializerSettings s_SerializationSettings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
        private readonly IAzureKeyVaultClient _keyVaultClient;
        private readonly bool _disposeClient;

        public AzureKeyVaultKeyValueAdapter() : this(new AzureKeyVaultClient(), true)
        {
        }

        public AzureKeyVaultKeyValueAdapter(IAzureKeyVaultClient keyVaultClient, bool disposeClient)
        {
            _keyVaultClient = keyVaultClient ?? throw new ArgumentNullException(nameof(keyVaultClient));
            _disposeClient = disposeClient;
        }

        /// <summary> Uses the managed identity to retrieve the actual value </summary>
        /// <param KeyValue ="IKeyValue">  inputs the IKeyValue </param>
        /// returns the keyname and actual value
        public async Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(IKeyValue keyValue, CancellationToken cancellationToken)
        {

            var keyValues = new List<KeyValuePair<string, string>>();

            string value = keyValue.Value;

            KeyVaultSecretReference secretRef = new KeyVaultSecretReference();
            try
            {
                secretRef = JsonConvert.DeserializeObject<KeyVaultSecretReference>(keyValue.Value, s_SerializationSettings);

            }
            catch (JsonReaderException)
            {
                string message = "Secret Reference was not initialized";
                Exception inner = new Exception();
                throw new KeyVaultReferenceException(message, inner);
            }


            //Get secret from KeyVault
            string secret = await _keyVaultClient.GetSecretValue(new Uri(secretRef.Uri, UriKind.Absolute), cancellationToken).ConfigureAwait(false);

            // add the key and it's value in the keyvaluePair
            keyValues.Add(new KeyValuePair<string, string>(keyValue.Key, secret));




            return keyValues;
        }

        public bool CanProcess(IKeyValue kv)
        {
            string contentType = kv?.ContentType?.Split(';')[0].Trim();

            return string.Equals(contentType, KeyVaultConstants.ContentType);
        }
    }
}