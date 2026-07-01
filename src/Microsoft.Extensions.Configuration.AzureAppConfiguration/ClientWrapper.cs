// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ClientWrapper
    {
        public ClientWrapper(Uri endpoint, ConfigurationClient configurationClient)
        {
            Endpoint = endpoint;
            ConfigurationClient = configurationClient;
        }

        public ClientWrapper(Uri endpoint, ConfigurationClient configurationClient, FeatureFlagClient featureFlagClient)
        {
            Endpoint = endpoint;
            ConfigurationClient = configurationClient;
            FeatureFlagClient = featureFlagClient;
        }

        public ConfigurationClient ConfigurationClient { get; private set; }

        public FeatureFlagClient FeatureFlagClient { get; private set; }

        public Uri Endpoint { get; private set; }
    }
}
