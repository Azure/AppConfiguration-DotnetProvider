// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault
{
    internal class AzureKeyVaultKeyValueAdapter : IKeyValueAdapter
    {
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
                throw KeyVaultReferenceException.Create("Invalid Key vault secret identifier.", setting, null, secretRefUri);
            }

            string secret = await _secretProvider.GetSecretValue(secretIdentifier, setting, logger, cancellationToken).ConfigureAwait(false);

            return new KeyValuePair<string, string>[]
            {
                new KeyValuePair<string, string>(setting.Key, secret)
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

            return setting.ContentType.TryParseContentType(out ContentType contentType)
                && contentType.IsKeyVaultReference();
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

        public async Task PreloadAsync(IEnumerable<ConfigurationSetting> settings, Logger logger, CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                return;
            }

            var seen = new HashSet<Uri>();
            var toFetch = new List<(KeyVaultSecretIdentifier, ConfigurationSetting)>();

            foreach (ConfigurationSetting setting in settings)
            {
                if (!CanProcess(setting))
                {
                    continue;
                }

                string secretRefUri = ParseSecretReferenceUri(setting);

                if (string.IsNullOrEmpty(secretRefUri) ||
                    !Uri.TryCreate(secretRefUri, UriKind.Absolute, out Uri secretUri) ||
                    !KeyVaultSecretIdentifier.TryCreate(secretUri, out KeyVaultSecretIdentifier secretIdentifier))
                {
                    throw KeyVaultReferenceException.Create("Invalid Key vault secret identifier.", setting, null, secretRefUri);
                }

                if (seen.Add(secretIdentifier.SourceId))
                {
                    toFetch.Add((secretIdentifier, setting));
                }
            }

            if (toFetch.Count == 0)
            {
                return;
            }

            if (_secretProvider.IsParallelSecretResolutionEnabled)
            {
                using (var semaphore = new SemaphoreSlim(KeyVaultConstants.MaxParallelSecretResolution))
                {
                    async Task ResolveSecretAsync(KeyVaultSecretIdentifier identifier, ConfigurationSetting setting)
                    {
                        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                        try
                        {
                            await _secretProvider.GetSecretValue(identifier, setting, logger, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }

                    Task[] tasks = new Task[toFetch.Count];

                    for (int i = 0; i < toFetch.Count; i++)
                    {
                        (KeyVaultSecretIdentifier identifier, ConfigurationSetting setting) = toFetch[i];
                        tasks[i] = ResolveSecretAsync(identifier, setting);
                    }

                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
            }
            else
            {
                foreach ((KeyVaultSecretIdentifier identifier, ConfigurationSetting setting) in toFetch)
                {
                    await _secretProvider.GetSecretValue(identifier, setting, logger, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private string ParseSecretReferenceUri(ConfigurationSetting setting)
        {
            string secretRefUri = null;

            try
            {
                var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(setting.Value));

                if (reader.Read() && reader.TokenType != JsonTokenType.StartObject)
                {
                    throw KeyVaultReferenceException.Create(ErrorMessages.InvalidKeyVaultReference, setting, null, null);
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
                            throw KeyVaultReferenceException.Create(ErrorMessages.InvalidKeyVaultReference, setting, null, null);
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
                throw KeyVaultReferenceException.Create(ErrorMessages.InvalidKeyVaultReference, setting, e, null);
            }

            return secretRefUri;
        }
    }
}
