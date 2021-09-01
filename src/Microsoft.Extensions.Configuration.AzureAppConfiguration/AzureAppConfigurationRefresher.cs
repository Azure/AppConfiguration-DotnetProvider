// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationRefresher : IConfigurationRefresher
    {
        private AzureAppConfigurationProvider _provider = null;
        private ILoggerFactory _loggerFactory;

        public Uri AppConfigurationEndpoint { get; private set; } = null;
   
        public ILoggerFactory LoggerFactory { 
            get => _loggerFactory; 
            set 
            { 
                ThrowIfNullProvider(nameof(LoggerFactory)); 
                _loggerFactory = value;

                // If IConfigurationRefresher.LoggerFactory is explicitly set, it should 
                // overwrite the LoggerFactory set in AzureAppConfigurationProvider
                _provider.LoggerFactory = _loggerFactory;
            } 
        }

        public void SetProvider(AzureAppConfigurationProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            AppConfigurationEndpoint = _provider.AppConfigurationEndpoint;

            // Save a copy of AzureAppConfigurationProvider.LoggerFactory if it 
            // was created during startup, so that it can be accessed later if needed
            _loggerFactory = _provider.LoggerFactory;
        }

        public async Task RefreshAsync()
        {
            ThrowIfNullProvider(nameof(RefreshAsync));
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

        public void SetDirty(TimeSpan? maxDelay)
        {
            ThrowIfNullProvider(nameof(SetDirty));
            _provider.SetDirty(maxDelay);
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
