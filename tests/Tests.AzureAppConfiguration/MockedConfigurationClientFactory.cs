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

        public MockedConfigurationClientFactory(Mock<ConfigurationClient> mockClient)
        {
            _mockClient = mockClient;
        }

        ConfigurationClient IConfigurationClientFactory.CreateConfigurationClient(string connectionString)
        {
            ConnectionStringCallCount++;
            return _mockClient.Object;
        }

        ConfigurationClient IConfigurationClientFactory.CreateConfigurationClient(Uri endpoint, TokenCredential credential)
        {
            TokenCredentialCallCount++;
            return _mockClient.Object;
        }
    }
}
