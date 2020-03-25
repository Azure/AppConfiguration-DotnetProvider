// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    class AzureKeyVaultKeyValueAdapter : IKeyValueAdapter
    {
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
            KeyVaultSecretReference secretRef;

            // Content validation
            try
            {
                secretRef = JsonSerializer.Deserialize<KeyVaultSecretReference>(setting.Value);
            }
            catch (JsonException e)
            {
                throw CreateKeyVaultReferenceException("Invalid Key Vault reference", setting, e, null);
            }

            // Uri validation
            if (string.IsNullOrEmpty(secretRef.Uri) || !Uri.TryCreate(secretRef.Uri, UriKind.Absolute, out Uri secretUri) || secretUri.Segments.Length < 3)
            {
                throw CreateKeyVaultReferenceException("Invalid Key vault secret identifier", setting, null, secretRef);
            }

            string secret;

            try
            {
                secret = await _secretProvider.GetSecretValue(secretUri, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is UnauthorizedAccessException || (e.Source?.Equals(AssemblyNames.AzureIdentity, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                throw CreateKeyVaultReferenceException(e.Message, setting, e, secretRef);
            }
            catch (Exception e) when (e is RequestFailedException || ((e as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false))
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
                ErrorCode = (inner as RequestFailedException)?.ErrorCode,
                SecretIdentifier = secretRef?.Uri
            };
        }

        public bool CanProcess(ConfigurationSetting setting)
        {
            string contentType = setting?.ContentType?.Split(';')[0].Trim();
            return string.Equals(contentType, KeyVaultConstants.ContentType);
        }
    }
}