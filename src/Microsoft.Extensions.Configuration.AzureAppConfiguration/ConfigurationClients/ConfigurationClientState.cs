// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.ConfigurationClients
{
    internal class ConfigurationClientState
    {
        private DateTimeOffset _backoffEndTime;
        private int _failedAttempts;

        public ConfigurationClientState(Uri endpoint)
        {
            this.Endpoint = endpoint;
            this._backoffEndTime = DateTimeOffset.UtcNow;
            this._failedAttempts = 0;
        }

        public Uri Endpoint { get; private set; }

        public bool IsAvailable()
        {
            return DateTimeOffset.UtcNow >= this._backoffEndTime;
        }

        public void UpdateConfigurationStoreStatus(bool requestSuccessful)
        {
            if (requestSuccessful)
            {
                this._backoffEndTime = DateTimeOffset.UtcNow;
                this._failedAttempts = 0;
            }
            else
            {
                this._failedAttempts++;
                TimeSpan backoffInterval = RetryConstants.DefaultMinBackoffInterval.CalculateBackoffInterval(this._failedAttempts);
                this._backoffEndTime = DateTimeOffset.UtcNow.Add(backoffInterval);
            }
        }
    }
}