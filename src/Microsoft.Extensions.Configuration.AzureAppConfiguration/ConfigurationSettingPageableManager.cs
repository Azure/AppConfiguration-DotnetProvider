using Azure.Data.AppConfiguration;
using Azure;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ConfigurationSettingPageableManager
    {
        public virtual IAsyncEnumerable<Page<ConfigurationSetting>> GetPages(AsyncPageable<ConfigurationSetting> pageable, IEnumerable<MatchConditions> matchConditions)
        {
            return pageable.AsPages(matchConditions);
        }

        public virtual IAsyncEnumerable<Page<ConfigurationSetting>> GetPages(AsyncPageable<ConfigurationSetting> pageable)
        {
            return pageable.AsPages();
        }
    }
}
