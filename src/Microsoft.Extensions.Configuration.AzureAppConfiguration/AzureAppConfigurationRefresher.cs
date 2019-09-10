using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    class AzureAppConfigurationRefresher : IConfigurationRefresher
    {
        private IList<IConfigurationRefresher> _refreshers;

        public AzureAppConfigurationRefresher()
        {
            _refreshers = new List<IConfigurationRefresher>();
        }

        public void Register(IConfigurationRefresher refresher)
        {
            _refreshers.Add(refresher);
        }

        public async Task Refresh()
        {
            if (!_refreshers.Any())
            {
                throw new InvalidOperationException("Refresh operation cannot be invoked before Azure App Configuration Provider is initialized.");
            }

            var refreshTasks = _refreshers.Select(r => r.Refresh());
            await Task.WhenAll(refreshTasks).ConfigureAwait(false);
        }
    }
}
