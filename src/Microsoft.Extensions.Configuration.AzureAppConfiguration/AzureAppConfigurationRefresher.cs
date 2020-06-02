// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationRefresher : IConfigurationRefresher
    {
        private AzureAppConfigurationProvider _provider = null;

        public void SetProvider(AzureAppConfigurationProvider provider)
        {
            _provider = provider;
        }

        public async Task RefreshAsync()
        {
            ThrowIfNullProvider();
            await _provider.RefreshAsync().ConfigureAwait(false);
        }

        public async Task<bool> TryRefreshAsync()
        {
            if (_provider == null)
            {
                return false;
            }

            return await _provider.TryRefreshAsync().ConfigureAwait(false);
        }

        public void ResetCache()
        {
            ThrowIfNullProvider();
            _provider.ResetCache();
        }

        private void ThrowIfNullProvider()
        {
            if (_provider == null)
            {
                throw new InvalidOperationException("ConfigurationBuilder.Build() must be called before this operation can be performed.");
            }
        }
    }
}
