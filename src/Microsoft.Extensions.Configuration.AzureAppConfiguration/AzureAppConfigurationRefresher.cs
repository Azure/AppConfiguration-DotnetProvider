﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System;
using System.Threading;
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

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            ThrowIfNullProvider(nameof(RefreshAsync));
            await _provider.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
        {
            if (_provider == null)
            {
                return false;
            }

            return await _provider.TryRefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        public void ProcessPushNotification(PushNotification pushNotification, TimeSpan? maxDelay)
        {
            ThrowIfNullProvider(nameof(ProcessPushNotification));

            _provider.ProcessPushNotification(pushNotification, maxDelay);
        }

        public void ProcessKeyValuePushNotification(KeyValuePushNotification keyValuePushNotification, TimeSpan? maxDelay)
        {
            ThrowIfNullProvider(nameof(ProcessKeyValuePushNotification));

            _provider.ProcessKeyValuePushNotification(keyValuePushNotification, maxDelay);
        }

        private void ThrowIfNullProvider(string operation)
        {
            if (_provider == null)
            {
                throw new InvalidOperationException($"ConfigurationBuilder.Build() must be called before {operation} can be accessed.");
            }
        }
    }
}
