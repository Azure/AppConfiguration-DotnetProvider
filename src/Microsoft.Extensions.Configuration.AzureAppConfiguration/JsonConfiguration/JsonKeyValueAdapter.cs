// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.JsonConfiguration
{
    internal class JsonKeyValueAdapter : IKeyValueAdapter
    {
        public Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, CancellationToken cancellationToken)
        {
            List<KeyValuePair<string, string>> keyValues = JsonConfigurationParser.ParseJsonSetting(setting);
            return Task.FromResult<IEnumerable<KeyValuePair<string, string>>>(keyValues);
        }

        public bool CanProcess(ConfigurationSetting setting)
        {
            if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
            {
                return false;
            }

            return JsonConfigurationParser.IsJsonContentType(setting.ContentType);
        }
    }
}

