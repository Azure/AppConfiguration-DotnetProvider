﻿using Azure.Data.AppConfiguration;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal enum KeyValueChangeType
    {
        None,
        Modified,
        Deleted
    }

    internal struct KeyValueChange
    {
        public KeyValueChangeType ChangeType { get; set; }

        public string Key { get; set; }

        public string Label { get; set; }

        public ConfigurationSetting Current { get; set; }
    }
}
