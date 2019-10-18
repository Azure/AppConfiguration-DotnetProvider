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
            // Workaround for '#' issue in Azure.Data.AppConfiguration 1.0.0-preview.3.  
            // This will be removed when provider moves to 1.0.0-preview.4 where issue is fixed.
            keyFilter = keyFilter.Replace("#", "%23");
            labelFilter = labelFilter.Replace("#", "%23");

            return new SettingSelector(keyFilter, labelFilter)
            {
                AsOf = asOf,
                Fields = fields
            };
        }
    }
}
