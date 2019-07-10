using Microsoft.Azure.AppConfiguration.Azconfig;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    class AzureKeyVaultKeyValueAdapter : IKeyValueAdapter, IDisposable
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

        public void Dispose()
        {
            if (_disposeClient)
            {
                (_keyVaultClient as IDisposable)?.Dispose();
            }
        }

        /// <summary> Uses the managed identity to retrieve the actual value </summary>
        /// <param KeyValue ="IKeyValue">  inputs the IKeyValue </param>
        /// returns the keyname and actual value
        public async Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(IKeyValue keyValue, CancellationToken cancellationToken)
        {
            string contentType = keyValue?.ContentType?.Split(';')[0].Trim();

            //Checking if the content type is our type (If not we return null)
            if (!string.Equals(contentType, KeyVaultConstants.ContentType))
            {
                return null;
            }

            KeyVaultSecretReference secretRef = JsonConvert.DeserializeObject<KeyVaultSecretReference>(keyValue.Value, s_SerializationSettings);

            //Get secret from KeyVault
            string secret = await _keyVaultClient.GetSecretValue(new Uri(secretRef.Uri, UriKind.Absolute), cancellationToken);

            var keyValues = new List<KeyValuePair<string, string>>();

            // add the key and it's value in the keyvaluePair
            keyValues.Add(new KeyValuePair<string, string>(keyValue.Key, secret));

            return keyValues;
        }
    }
}