using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal enum KeyValueChangeType
    {
        Modified,
        Deleted
    }

    // TODO: Struct
    internal class KeyValueChange
    {
        public KeyValueChangeType ChangeType { get; set; }

        public string Key { get; set; }

        public string Label { get; set; }

        public ConfigurationSetting Current { get; set; }
    }
}
