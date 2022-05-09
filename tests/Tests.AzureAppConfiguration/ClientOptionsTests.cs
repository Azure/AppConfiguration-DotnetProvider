// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Testing;
using Microsoft.Extensions.Configuration;
using System;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class ClientOptionsTests
    {
        [Fact]
        public void ClientOptionsTests_OverridesDefaultClientOptions()
        {
            // Arrange
            MockTransport mockTransport = new MockTransport(new MockResponse(429), new MockResponse(429), new MockResponse(429));
            var requestCountPolicy = new HttpRequestCountPipelinePolicy();
            int defaultMaxRetries = 0;

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(TestHelpers.CreateMockEndpointString());
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                    defaultMaxRetries = options.ClientOptions.Retry.MaxRetries;
                });

            // Act - Build
            Assert.Throws<AggregateException>(configBuilder.Build);

            // Assert defaultMaxRetries + 1 original request = totalRequestCount
            Assert.Equal(defaultMaxRetries + 1, requestCountPolicy.RequestCount);

            requestCountPolicy.ResetRequestCount();

            // Arrange
            int maxRetries = defaultMaxRetries + 1;
            mockTransport = new MockTransport(new MockResponse(429), new MockResponse(429), new MockResponse(429), new MockResponse(429));

            configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(TestHelpers.CreateMockEndpointString());
                    options.ConfigureClientOptions(clientOptions => clientOptions.Retry.MaxRetries = maxRetries);
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                });

            // Act - Build
            Assert.Throws<AggregateException>(configBuilder.Build);

            // Assert maxRetries + 1 original request = totalRequestCount.
            Assert.Equal(maxRetries + 1, requestCountPolicy.RequestCount);
        }
    }
}
