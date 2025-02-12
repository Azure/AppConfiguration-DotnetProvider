using Azure.Data.AppConfiguration;
using Azure;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    static class ConfigurationSettingPageExtensions
    {
        public static IAsyncEnumerable<Page<ConfigurationSetting>> AsPages(this AsyncPageable<ConfigurationSetting> pageable, IConfigurationSettingPageIterator pageIterator)
        {
            //
            // Allow custom iteration
            if (pageIterator != null)
            {
                return pageIterator.IteratePages(pageable);
            }

            return pageable.AsPages();
        }

        public static IAsyncEnumerable<Page<ConfigurationSetting>> AsPages(this AsyncPageable<ConfigurationSetting> pageable, IConfigurationSettingPageIterator pageIterator, IEnumerable<MatchConditions> matchConditions)
        {
            //
            // Allow custom iteration
            if (pageIterator != null)
            {
                return pageIterator.IteratePages(pageable, matchConditions);
            }

            return pageable.AsPages(matchConditions);
        }
    }
}
