using Azure.Data.AppConfiguration;
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
        public async Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, CancellationToken cancellationToken)
        {

            KeyVaultSecretReference secretRef = null;

            //
            // Content validation
            try
            {
                secretRef = JsonConvert.DeserializeObject<KeyVaultSecretReference>(setting.Value, s_SerializationSettings);

            }
            catch (JsonReaderException e)
            {
                throw CreateKeyVaultReferenceException("Invalid Key Vault reference", setting, e, null);
            }

            // Uri validation
            if (string.IsNullOrEmpty(secretRef.Uri) ||
                !Uri.TryCreate(secretRef.Uri, UriKind.Absolute, out Uri secretUri))
            {
                throw CreateKeyVaultReferenceException("Invalid Key vault secret identifier", setting, null, secretRef);
            }

            string secret = null;

            try
            {
                secret = await _secretProvider.GetSecretValue(secretUri, cancellationToken).ConfigureAwait(false);
            }
            catch (KeyVaultErrorException e)
            {
                throw CreateKeyVaultReferenceException("Key vault error", setting, e, secretRef);
            }


            return new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string, string>(setting.Key, secret)
            };
        }

        KeyVaultReferenceException CreateKeyVaultReferenceException(string message, ConfigurationSetting setting, Exception inner, KeyVaultSecretReference secretRef = null)
        {
            return new KeyVaultReferenceException(message, inner)
            {
                Key = setting.Key,
                Label = setting.Label,
                Etag = setting.ETag.ToString(),
                ErrorCode = (inner as KeyVaultErrorException)?.Body?.Error?.InnerError?.Code,
                SecretIdentifier = secretRef?.Uri,

            };
        }

        public bool CanProcess(ConfigurationSetting setting)
        {
            string contentType = setting?.ContentType?.Split(';')[0].Trim();

            return string.Equals(contentType, KeyVaultConstants.ContentType);
        }
    }
}