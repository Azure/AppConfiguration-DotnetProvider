using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;

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
            SettingSelector selector = new SettingSelector()
            {
                AsOf = asOf,
                Fields = fields
            };

            selector.Keys.Clear();
            selector.Labels.Clear();

            // Convert from comma literal format to SDK convention
            var keyFilters = ParseFilters(keyFilter);

            foreach (var filter in keyFilters)
            {
                selector.Keys.Add(filter);
            }

            selector.Labels.Add(labelFilter);

            return selector;
        }

        private static IEnumerable<string> ParseFilters(string filterString)
        {
            var filters = new List<string>();
            var prev = ' ';
            var startIdx = 0;
            for (int i = 0; i < filterString.Length; i++)
            {
                var c = filterString[i];
                if (c == ',' && prev != '\\')
                {
                    if (i - startIdx > 0)
                    {
                        filters.Add(filterString.Substring(startIdx, i - startIdx).Replace("\\,", ","));
                    }
                    startIdx = i + 1;
                }

                prev = c;
            }

            if (filterString.Length - startIdx > 0)
            {
                filters.Add(filterString.Substring(startIdx, filterString.Length - startIdx).Replace("\\,", ","));
            }

            return filters.Where(f => !string.IsNullOrEmpty(f));
        }
    }
}
