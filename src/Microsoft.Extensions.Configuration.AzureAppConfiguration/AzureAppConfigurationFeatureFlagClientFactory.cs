// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class AzureAppConfigurationFeatureFlagClientFactory : IAzureClientFactory<FeatureFlagClient>
    {
        private readonly FeatureFlagClientOptions _clientOptions;

        private readonly TokenCredential _credential;
        private readonly IEnumerable<string> _connectionStrings;

        public AzureAppConfigurationFeatureFlagClientFactory(
            IEnumerable<string> connectionStrings,
            FeatureFlagClientOptions clientOptions)
        {
            if (connectionStrings == null || !connectionStrings.Any())
            {
                throw new ArgumentNullException(nameof(connectionStrings));
            }

            _connectionStrings = connectionStrings;

            _clientOptions = clientOptions ?? throw new ArgumentNullException(nameof(clientOptions));
        }

        public AzureAppConfigurationFeatureFlagClientFactory(
            TokenCredential credential,
            FeatureFlagClientOptions clientOptions)
        {
            _credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _clientOptions = clientOptions ?? throw new ArgumentNullException(nameof(clientOptions));
        }

        public FeatureFlagClient CreateClient(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uriResult))
            {
                throw new ArgumentException("Invalid host URI.");
            }

            if (_credential != null)
            {
                return new FeatureFlagClient(uriResult, _credential, _clientOptions);
            }

            string connectionString = _connectionStrings.FirstOrDefault(cs => ConnectionStringUtils.Parse(cs, ConnectionStringUtils.EndpointSection) == endpoint);

            //
            // fallback to the first connection string
            if (connectionString == null)
            {
                string id = ConnectionStringUtils.Parse(_connectionStrings.First(), ConnectionStringUtils.IdSection);
                string secret = ConnectionStringUtils.Parse(_connectionStrings.First(), ConnectionStringUtils.SecretSection);

                connectionString = ConnectionStringUtils.Build(uriResult, id, secret);
            }

            return new FeatureFlagClient(connectionString, _clientOptions);
        }
    }
}
