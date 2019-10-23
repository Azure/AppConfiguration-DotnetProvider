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
            // Convert from comma literal format to SDK convention
            string[] keyFilters = keyFilter?.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

            SettingSelector selector = new SettingSelector()
            {
                AsOf = asOf,
                Fields = fields
            };

            selector.Keys.Clear();
            selector.Labels.Clear();

            foreach (var filter in keyFilters)
            {
                selector.Keys.Add(filter);
            }

            selector.Labels.Add(labelFilter);

            return selector;
        }
    }
}
