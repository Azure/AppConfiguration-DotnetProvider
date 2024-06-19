// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class JsonKeyValueAdapter : IKeyValueAdapter
    {
        private static readonly IEnumerable<string> ExcludedJsonContentTypes = new[] 
        {
            FeatureManagementConstants.ContentType,
            KeyVaultConstants.ContentType
        };

        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Uri endpoint, Logger logger, CancellationToken cancellationToken)
        {
            if (setting == null)
            {
                throw new ArgumentNullException(nameof(setting));
            }

            string rootJson = $"{{\"{setting.Key}\":{setting.Value}}}";
            JsonElement jsonData;
            try
            {
                jsonData = JsonSerializer.Deserialize<JsonElement>(rootJson);
            }
            catch (JsonException)
            {
                // If the value is not a valid JSON, treat it like regular string value
                return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(new[] { new KeyValuePair<string, string>(setting.Key, setting.Value) });
            }

            return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(new JsonFlattener().FlattenJson(jsonData));
        }

        public bool CanProcess(ConfigurationSetting setting)
        {
            if (setting == null ||
                string.IsNullOrWhiteSpace(setting.Value) ||
                string.IsNullOrWhiteSpace(setting.ContentType))
            {
                return false;
            }

            string acceptedMainType = "application";
            string acceptedSubType = "json";
            string mediaType;

            try
            {
                mediaType = new ContentType(setting.ContentType.Trim()).MediaType;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (IndexOutOfRangeException)
            {
                // Bug in System.Net.Mime.ContentType throws this if contentType is "xyz/"
                // https://github.com/dotnet/runtime/issues/39337
                return false;
            }

            if (!ExcludedJsonContentTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase))
            {
                // Since contentType has been validated using System.Net.Mime.ContentType,
                // mediaType will always have exactly 2 parts after splitting on '/'
                string[] types = mediaType.Split('/');
                if (string.Equals(types[0], acceptedMainType, StringComparison.OrdinalIgnoreCase))
                {
                    string[] subTypes = types[1].Split('+');
                    if (subTypes.Contains(acceptedSubType, StringComparer.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void OnConfigurationRefresh(ConfigurationSetting setting = null)
        {
            return;
        }

        public void OnConfigurationUpdated()
        {
            return;
        }

        public bool NeedsRefresh()
        {
            return false;
        }

        public void ResetState()
        {
            return;
        }
    }
}
