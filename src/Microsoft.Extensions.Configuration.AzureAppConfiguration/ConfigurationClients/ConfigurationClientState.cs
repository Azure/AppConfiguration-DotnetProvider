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
        private DateTimeOffset _retryAfterTimeout;
        private int _failedAttempts;

        public ConfigurationClientState(Uri endpoint)
        {
            this.Endpoint = endpoint;
            this._retryAfterTimeout = DateTimeOffset.UtcNow;
            this._failedAttempts = 0;
        }

        public Uri Endpoint { get; private set; }

        public bool IsAvailable()
        {
            return DateTimeOffset.UtcNow >= this._retryAfterTimeout;
        }

        public void UpdateConfigurationStoreStatus(bool requestSuccessful)
        {
            if (requestSuccessful)
            {
                this._retryAfterTimeout = DateTimeOffset.UtcNow;
                this._failedAttempts = 0;
            }
            else
            {
                this._failedAttempts++;
                var retryAfterTimeout = RetryConstants.DefaultMinRetryAfter.CalculateRetryAfterTime(this._failedAttempts);
                this._retryAfterTimeout = DateTimeOffset.UtcNow.Add(retryAfterTimeout);
            }
        }
    }
}