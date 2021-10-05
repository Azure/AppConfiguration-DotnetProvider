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

        public Uri AppConfigurationEndpoint { get; private set; } = null;

        public void SetProvider(AzureAppConfigurationProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            AppConfigurationEndpoint = _provider.AppConfigurationEndpoint;
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

       /// <summary>
       /// calls processPushNotification in the provider to update Sync Token and call setDirty()
       /// </summary>
       /// <param name="pushNotification"></param>
       /// <param name="maxDelay"></param>
       /// <returns></returns>
        public void ProcessPushNotification(PushNotification pushNotification, TimeSpan? maxDelay)
        {
            ThrowIfNullProvider();

            _provider.ProcessPushNotification(pushNotification, maxDelay);
        }

        public void SetDirty(TimeSpan? maxDelay)
        {
            ThrowIfNullProvider();
            _provider.SetDirty(maxDelay);
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
