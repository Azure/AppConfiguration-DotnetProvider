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
        private readonly AzureKeyVaultSecretProvider _secretProvider;


        public AzureKeyVaultKeyValueAdapter(AzureKeyVaultSecretProvider secretProvider)
        {
            _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
        }

        /// <summary> Uses the managed identity to retrieve the actual value </summary>
        /// <param KeyValue ="IKeyValue">  inputs the IKeyValue </param>
        /// returns the keyname and actual value
        public async Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(IKeyValue keyValue, CancellationToken cancellationToken)
        {

            var keyValues = new List<KeyValuePair<string, string>>();

            string value = keyValue.Value;

            KeyVaultSecretReference secretRef = null;

            try
            {
                secretRef = JsonConvert.DeserializeObject<KeyVaultSecretReference>(keyValue.Value, s_SerializationSettings);

            }
            catch (JsonReaderException e)
            {
                throw new KeyVaultReferenceException("Invalid KeyValult reference", e);
            }


            //Get secret from KeyVault
            string secret = await _secretProvider.GetSecretValue(new Uri(secretRef.Uri, UriKind.Absolute), cancellationToken).ConfigureAwait(false);

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