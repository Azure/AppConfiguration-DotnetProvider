using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Azure.KeyVault.Models;
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

        /// <summary> Uses the Azure Key Vault secret provider to resolve Key Vault references retrieved from Azure App Configuration. </summary>
        /// <param KeyValue ="IKeyValue">  inputs the IKeyValue </param>
        /// returns the keyname and actual value
        public async Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(IKeyValue kv, CancellationToken cancellationToken)
        {

            KeyVaultSecretReference secretRef = null;

            //
            // Content validation
            try
            {
                secretRef = JsonConvert.DeserializeObject<KeyVaultSecretReference>(kv.Value, s_SerializationSettings);

            }
            catch (JsonReaderException e)
            {
                throw CreateKeyVaultReferenceException("Invalid Key Vault reference", kv, e, null);
            }

            // Uri validation
            if (string.IsNullOrEmpty(secretRef.Uri) ||
                !Uri.TryCreate(secretRef.Uri, UriKind.Absolute, out Uri secretUri))
            {
                throw CreateKeyVaultReferenceException("Invalid Key vault secret identifier", kv, null, secretRef);
            }

            string secret = null;

            try
            {
                secret = await _secretProvider.GetSecretValue(secretUri, cancellationToken).ConfigureAwait(false);
            }
            catch (KeyVaultErrorException e)
            {
                throw CreateKeyVaultReferenceException("Key vault error", kv, e, secretRef);
            }


            return new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string, string>(kv.Key, secret)
            };
        }

        KeyVaultReferenceException CreateKeyVaultReferenceException(string message, IKeyValue kv, Exception inner, KeyVaultSecretReference secretRef = null)
        {
            return new KeyVaultReferenceException(message, inner)
            {
                Key = kv.Key,
                Label = kv.Label,
                Etag = kv.ETag,
                ErrorCode = (inner as KeyVaultErrorException)?.Body?.Error?.InnerError?.Code,
                SecretIdentifier = secretRef?.Uri,

            };
        }

        public bool CanProcess(IKeyValue kv)
        {
            string contentType = kv?.ContentType?.Split(';')[0].Trim();

            return string.Equals(contentType, KeyVaultConstants.ContentType);
        }
    }
}