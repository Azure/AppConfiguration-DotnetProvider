using Azure;
using Azure.Data.AppConfiguration;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IConfigurationSettingPageIterator
    {
        IAsyncEnumerable<Page<ConfigurationSetting>> IteratePages(AsyncPageable<ConfigurationSetting> pageable);

        IAsyncEnumerable<Page<ConfigurationSetting>> IteratePages(AsyncPageable<ConfigurationSetting> pageable, IEnumerable<MatchConditions> matchConditions);
    }
}
