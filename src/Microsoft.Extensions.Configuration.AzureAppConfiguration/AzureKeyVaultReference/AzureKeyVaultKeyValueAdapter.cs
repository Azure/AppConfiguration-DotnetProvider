// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    internal class AzureKeyVaultKeyValueAdapter : IKeyValueAdapter
    {
        private const string AzureIdentityAssemblyName = "Azure.Identity";

        private readonly AzureKeyVaultSecretProvider _secretProvider;

        public AzureKeyVaultKeyValueAdapter(AzureKeyVaultSecretProvider secretProvider)
        {
            _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
        }

        /// <summary> Uses the Azure Key Vault secret provider to resolve Key Vault references retrieved from Azure App Configuration. </summary>
        /// <param KeyValue ="IKeyValue">  inputs the IKeyValue </param>
        /// returns the keyname and actual value
        public async Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Uri endpoint, Logger logger, CancellationToken cancellationToken)
        {
            string secretRefUri = ParseSecretReferenceUri(setting);

            // Uri validation
            if (string.IsNullOrEmpty(secretRefUri) || !Uri.TryCreate(secretRefUri, UriKind.Absolute, out Uri secretUri) || !KeyVaultSecretIdentifier.TryCreate(secretUri, out KeyVaultSecretIdentifier secretIdentifier))
            {
                throw CreateKeyVaultReferenceException("Invalid Key vault secret identifier.", setting, null, secretRefUri);
            }

            string secret;

            try
            {
                secret = await _secretProvider.GetSecretValue(secretIdentifier, setting.Key, setting.Label, logger, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is UnauthorizedAccessException || (e.Source?.Equals(AzureIdentityAssemblyName, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                throw CreateKeyVaultReferenceException(e.Message, setting, e, secretRefUri);
            }
            catch (Exception e) when (e is RequestFailedException || ((e as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false))
            {
                throw CreateKeyVaultReferenceException("Key vault error.", setting, e, secretRefUri);
            }

            return new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string, string>(setting.Key, secret)
            };
        }

        KeyVaultReferenceException CreateKeyVaultReferenceException(string message, ConfigurationSetting setting, Exception inner, string secretRefUri = null)
        {
            return new KeyVaultReferenceException(message, inner)
            {
                Key = setting.Key,
                Label = setting.Label,
                Etag = setting.ETag.ToString(),
                ErrorCode = (inner as RequestFailedException)?.ErrorCode,
                SecretIdentifier = secretRefUri
            };
        }

        public bool CanProcess(ConfigurationSetting setting)
        {
            if (setting == null ||
                string.IsNullOrWhiteSpace(setting.Value) ||
                string.IsNullOrWhiteSpace(setting.ContentType))
            {
                return false;
            }

            if (setting.ContentType.TryParseContentType(out ContentType contentType))
            {
                return contentType.IsKeyVaultReference();
            }

            return false;
        }

        public void OnChangeDetected(ConfigurationSetting setting = null)
        {
            if (setting == null)
            {
                _secretProvider.ClearCache();
            }
            else
            {
                if (CanProcess(setting))
                {
                    string secretRefUri = ParseSecretReferenceUri(setting);

                    if (!string.IsNullOrEmpty(secretRefUri) && Uri.TryCreate(secretRefUri, UriKind.Absolute, out Uri secretUri) && KeyVaultSecretIdentifier.TryCreate(secretUri, out KeyVaultSecretIdentifier secretIdentifier))
                    {
                        _secretProvider.RemoveSecretFromCache(secretIdentifier.SourceId);
                    }
                }
            }
        }

        public void OnConfigUpdated()
        {
            return;
        }

        public bool NeedsRefresh()
        {
            return _secretProvider.ShouldRefreshKeyVaultSecrets();
        }

        private string ParseSecretReferenceUri(ConfigurationSetting setting)
        {
            string secretRefUri = null;

            try
            {
                var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(setting.Value));

                if (reader.Read() && reader.TokenType != JsonTokenType.StartObject)
                {
                    throw CreateKeyVaultReferenceException(ErrorMessages.InvalidKeyVaultReference, setting, null, null);
                }

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    if (reader.GetString() == KeyVaultConstants.SecretReferenceUriJsonPropertyName)
                    {
                        if (reader.Read() && reader.TokenType == JsonTokenType.String)
                        {
                            secretRefUri = reader.GetString();
                        }
                        else
                        {
                            throw CreateKeyVaultReferenceException(ErrorMessages.InvalidKeyVaultReference, setting, null, null);
                        }
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
            }
            catch (JsonException e)
            {
                throw CreateKeyVaultReferenceException(ErrorMessages.InvalidKeyVaultReference, setting, e, null);
            }

            return secretRefUri;
        }
    }
}
