// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Data.AppConfiguration.Tests;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class GeoReplicationTests
    {
        ConfigurationSetting _kv = ConfigurationModelFactory.ConfigurationSetting("TestKey1", "newTestValue1", "test",
                                    eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c6"),
                                    contentType: "text");

        [Fact]
        public async Task VerifyFallbackClientUsedWhenPrimaryDown()
        {
            var mockTransport = new MockTransport(req =>
            {
                if (req.Uri.Host.Equals(TestHelpers.PrimaryConfigStoreEndpoint.Host))
                {
                    return new MockResponse(503);
                }
                else
                {
                    var response = new MockResponse(200);
                    response.SetContent(SerializationHelpers.Serialize(new[] { _kv }, TestHelpers.SerializeBatch));
                    return response;
                }
            });

            var clientOptions = new ConfigurationClientOptions
            {
                Transport = mockTransport
            };

            var configClient = TestHelpers.CreateMockConfigurationClient(clientOptions);

            await configClient.GetConfigurationSettingAsync(SettingSelector.Any);

            Assert.Contains(mockTransport.Requests, r => r.Uri.Host.Equals(TestHelpers.SecondaryConfigStoreEndpoint.Host));
        }

        [Fact]
        public async Task VerifyPrimaryEndpointRetriedAfterBackOff()
        {
            var mockTransport = new MockTransport(req =>
            {
                if (req.Uri.Host.Equals(TestHelpers.PrimaryConfigStoreEndpoint.Host))
                {
                    return new MockResponse(503);
                }
                else
                {
                    var response = new MockResponse(200);
                    response.SetContent(SerializationHelpers.Serialize(new[] { _kv }, TestHelpers.SerializeBatch));
                    return response;
                }
            });

            var clientOptions = new ConfigurationClientOptions
            {
                Transport = mockTransport
            };

            var configClient = TestHelpers.CreateMockConfigurationClient(clientOptions);

            await configClient.GetConfigurationSettingAsync(SettingSelector.Any);

            // Since primary config store is down, the requests should be made to both primary and the secondary store.
            Assert.Contains(mockTransport.Requests, r => r.Uri.Host.Equals(TestHelpers.PrimaryConfigStoreEndpoint.Host));
            Assert.Contains(mockTransport.Requests, r => r.Uri.Host.Equals(TestHelpers.SecondaryConfigStoreEndpoint.Host));

            mockTransport.ResetRequests();

            await configClient.GetConfigurationSettingAsync(SettingSelector.Any);
            Thread.Sleep(TimeSpan.FromSeconds(10));
            configClient.GetConfigurationSettingsAsync(new SettingSelector());

            // After we detect the primary store is down, all future requests should go to secondary store until backoff time is reached.
            Assert.All(mockTransport.Requests, r => r.Uri.Host.Equals(TestHelpers.SecondaryConfigStoreEndpoint.Host));

            // Backoff time for attempt 1 would be in the range of 30 seconds to 1 minute from the time request failed. So wait for a minute (10 seconds + 50 seconds) and retry.
            Thread.Sleep(TimeSpan.FromSeconds(50));

            mockTransport.ResetRequests();
            await configClient.GetConfigurationSettingAsync(SettingSelector.Any);

            // Since backoff time has passed, the request should be made to the primary configuration store.
            Assert.Contains(mockTransport.Requests, r => r.Uri.Host.Equals(TestHelpers.PrimaryConfigStoreEndpoint.Host));
        }
    }
}
