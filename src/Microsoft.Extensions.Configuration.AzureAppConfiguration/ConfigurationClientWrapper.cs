// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ConfigurationClientWrapper
    {
        public ConfigurationClientWrapper(Uri endpoint, ConfigurationClient configurationClient, bool isDiscovered = false)
        {
            Endpoint = endpoint;
            Client = configurationClient;
            BackoffEndTime = default;
            FailedAttempts = 0;
            IsDiscoveredClient = isDiscovered;
        }

        public int FailedAttempts { get; set; }
        public DateTimeOffset BackoffEndTime { get; set; }
        public ConfigurationClient Client { get; private set; }
        public Uri Endpoint { get; private set; }
        public bool IsDiscoveredClient { get; private set; }
    }
}
