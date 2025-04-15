// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class JsonKeyValueAdapter : IKeyValueAdapter
    {
        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, Uri endpoint, Logger logger, CancellationToken cancellationToken)
        {
            if (setting == null)
            {
                throw new ArgumentNullException(nameof(setting));
            }

            string rootJson = $"{{\"{setting.Key}\":{setting.Value}}}";

            List<KeyValuePair<string, string>> keyValuePairs = new List<KeyValuePair<string, string>>();

            try
            {
                using (JsonDocument document = JsonDocument.Parse(rootJson))
                {
                    keyValuePairs = new JsonFlattener().FlattenJson(document.RootElement);
                }
            }
            catch (JsonException)
            {
                // If the value is not a valid JSON, treat it like regular string value
                return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(new[] { new KeyValuePair<string, string>(setting.Key, setting.Value) });
            }

            return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(keyValuePairs);
        }

        public bool CanProcess(ConfigurationSetting setting)
        {
            if (setting == null ||
                string.IsNullOrWhiteSpace(setting.Value) ||
                string.IsNullOrWhiteSpace(setting.ContentType))
            {
                return false;
            }

            return setting.ContentType.IsJsonContentType();
        }

        public void OnChangeDetected(ConfigurationSetting setting = null)
        {
            return;
        }

        public void OnConfigUpdated()
        {
            return;
        }

        public bool NeedsRefresh()
        {
            return false;
        }
    }
}
