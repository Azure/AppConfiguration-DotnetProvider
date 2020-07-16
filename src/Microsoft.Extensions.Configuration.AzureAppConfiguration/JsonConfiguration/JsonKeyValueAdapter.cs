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
            IEnumerable<KeyValuePair<string, string>> keyValues = JsonConfigurationParser.Parse(setting);
            return Task.FromResult(keyValues);
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

