// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Data.AppConfiguration.Tests;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class FailoverTests
    {
        ConfigurationSetting _kv = ConfigurationModelFactory.ConfigurationSetting("TestKey1", "newTestValue1", "test",
                                    eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c6"),
                                    contentType: "text");

        [Fact]
        public async Task VerifyFallbackClientUsedWhenFirstFails()
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

            var options = new AzureAppConfigurationOptions();
            options.ClientOptions.Transport = mockTransport;

            var configClient = TestHelpers.CreateMockConfigurationClient(options);

            await configClient.GetConfigurationSettingAsync(SettingSelector.Any);

            Assert.Contains(mockTransport.Requests, r => r.Uri.Host.Equals(TestHelpers.SecondaryConfigStoreEndpoint.Host));
        }
    }
}
