using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class SelectorFactory
    {
        public static SettingSelector CreateSettingSelector()
        {
            return new SettingSelector();
        }

        public static SettingSelector CreateSettingSelector(string keyFilter, string labelFilter, DateTimeOffset? asOf = default, SettingFields fields = SettingFields.All)
        {
            return new SettingSelector(keyFilter, labelFilter)
            {
                AsOf = asOf,
                Fields = fields
            };
        }
    }
}
