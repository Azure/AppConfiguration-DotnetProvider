using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

        public async Task Refresh(CancellationToken cancellationToken)
        {
            if (!_refreshers.Any())
            {
                throw new InvalidOperationException("Refresh operation cannot be invoked before Azure App Configuration Provider is initialized.");
            }

            await _refreshers.ParallelForEachAsync(r => r.Refresh(cancellationToken), maxDegreeOfParallelism: 4).ConfigureAwait(false);
        }
    }
}
