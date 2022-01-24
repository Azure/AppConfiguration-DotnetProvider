// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tests.AzureAppConfiguration
{
    class MockedConfigurationClientFactory : IConfigurationClientFactory
    {
        private readonly Mock<IConfigurationClient> _mockClient;
        internal int ConnectionStringCallCount { get; set; } = 0;
        internal int TokenCredentialCallCount { get; set; } = 0;
        internal ConfigurationClientOptions ClientOptions;

        public MockedConfigurationClientFactory(Mock<IConfigurationClient> mockClient)
        {
            _mockClient = mockClient;
        }

        public IConfigurationClient CreateConfigurationClient(string connectionString, ConfigurationClientOptions clientOptions)
        {
            ClientOptions = clientOptions;
            ConnectionStringCallCount++;
            return _mockClient.Object;
        }

        public IConfigurationClient CreateConfigurationClient(IEnumerable<Uri> endpoints, TokenCredential credential, ConfigurationClientOptions clientOptions)
        {
            ClientOptions = clientOptions;
            TokenCredentialCallCount += endpoints.Count();
            return _mockClient.Object;
        }
    }
}
