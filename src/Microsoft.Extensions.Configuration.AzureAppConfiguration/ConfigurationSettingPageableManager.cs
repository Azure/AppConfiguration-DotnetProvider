using Azure.Data.AppConfiguration;
using Azure;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ConfigurationSettingPageableManager
    {
        public virtual IAsyncEnumerable<Page<ConfigurationSetting>> GetPages(AsyncPageable<ConfigurationSetting> pageable, IEnumerable<MatchConditions> matchConditions)
        {
            return pageable.AsPages(matchConditions);
        }
    }
}
