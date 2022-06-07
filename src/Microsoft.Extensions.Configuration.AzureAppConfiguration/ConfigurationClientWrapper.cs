// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ConfigurationClientWrapper
    {
        public ConfigurationClientWrapper(Uri endpoint, ConfigurationClient configurationClient)
        {
            Endpoint = endpoint;
            Client = configurationClient;
            BackoffEndTime = DateTimeOffset.UtcNow;
            FailedAttempts = 0;
        }

        public int FailedAttempts { get; set; }
        public DateTimeOffset BackoffEndTime { get; set; }
        public ConfigurationClient Client { get; private set; }
        public Uri Endpoint { get; private set; }
    }
}
