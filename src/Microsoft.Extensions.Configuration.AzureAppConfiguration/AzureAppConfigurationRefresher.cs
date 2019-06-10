using System;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationRefresher : IConfigurationRefresher
    {
        private AzureAppConfigurationProvider _provider = null;

        internal void SetProvider(AzureAppConfigurationProvider provider)
        {
            _provider = provider;
        }

        public async Task Refresh()
        {
            if (_provider == null)
            {
                throw new InvalidOperationException("Refresh operation cannot be invoked before Azure App Configuration Provider is initialized.");
            }

            await _provider.RefreshKeyValues();
        }
    }
}
