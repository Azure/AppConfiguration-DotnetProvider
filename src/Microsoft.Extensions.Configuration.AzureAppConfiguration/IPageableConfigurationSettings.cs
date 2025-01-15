using Azure.Data.AppConfiguration;
using Azure;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IPageableConfigurationSettings
    {
        IAsyncEnumerable<Page<ConfigurationSetting>> IteratePages(AsyncPageable<ConfigurationSetting> pageable);

        IAsyncEnumerable<Page<ConfigurationSetting>> IteratePages(AsyncPageable<ConfigurationSetting> pageable, IEnumerable<MatchConditions> matchConditions);
    }

    static class ConfigurationSettingPageExtensions
    {
        public static IAsyncEnumerable<Page<ConfigurationSetting>> AsPages(this AsyncPageable<ConfigurationSetting> pageable, IPageableConfigurationSettings pageableConfigurationSettings)
        {
            //
            // Allow custom iteration
            if (pageableConfigurationSettings != null)
            {
                return pageableConfigurationSettings.IteratePages(pageable);
            }

            return pageable.AsPages();
        }

        public static IAsyncEnumerable<Page<ConfigurationSetting>> AsPages(this AsyncPageable<ConfigurationSetting> pageable, IPageableConfigurationSettings pageableConfigurationSettings, IEnumerable<MatchConditions> matchConditions)
        {
            //
            // Allow custom iteration
            if (pageableConfigurationSettings != null)
            {
                return pageableConfigurationSettings.IteratePages(pageable, matchConditions);
            }

            return pageable.AsPages(matchConditions);
        }
    }
}
