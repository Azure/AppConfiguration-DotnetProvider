// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;

namespace Tests.AzureAppConfiguration
{
    class MockedConfigurationClientFactory : IConfigurationClientFactory
    {
        private readonly Mock<ConfigurationClient> _mockClient;
        internal int ConnectionStringCallCount { get; set; } = 0;
        internal int TokenCredentialCallCount { get; set; } = 0;
        internal ConfigurationClientOptions ClientOptions;

        public MockedConfigurationClientFactory(Mock<ConfigurationClient> mockClient)
        {
            _mockClient = mockClient;
        }

        public ConfigurationClient CreateConfigurationClient(string connectionString, ConfigurationClientOptions clientOptions)
        {
            ClientOptions = clientOptions;
            ConnectionStringCallCount++;
            return _mockClient.Object;
        }

        public ConfigurationClient CreateConfigurationClient(Uri endpoint, TokenCredential credential, ConfigurationClientOptions clientOptions)
        {
            ClientOptions = clientOptions;
            TokenCredentialCallCount++;
            return _mockClient.Object;
        }
    }
}
