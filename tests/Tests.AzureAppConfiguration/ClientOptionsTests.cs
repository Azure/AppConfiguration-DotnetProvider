// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class ClientOptionsTests
    {
        [Fact]
        public void ClientOptionsTests_OverridesDefaultClientOptions()
        {
            // Arrange
            var requestCountPolicy = new HttpRequestCountPipelinePolicy();
            int defaultMaxRetries = 0;

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Startup.Timeout = TimeSpan.FromSeconds(15);
                    options.Connect(TestHelpers.CreateMockEndpointString());
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                    defaultMaxRetries = options.ClientOptions.Retry.MaxRetries;
                });

            // Act - Build
            Assert.Throws<AggregateException>(configBuilder.Build);

            var exponentialRequestCount = requestCountPolicy.RequestCount;

            requestCountPolicy.ResetRequestCount();

            // Arrange
            int maxRetries = defaultMaxRetries + 1;

            configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Startup.Timeout = TimeSpan.FromSeconds(15);
                    options.Connect(TestHelpers.CreateMockEndpointString());
                    options.ConfigureClientOptions(clientOptions => clientOptions.Retry.Delay = TimeSpan.FromSeconds(60));
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                });

            // Act - Build
            Assert.Throws<TaskCanceledException>(configBuilder.Build);

            // Assert less retries due to increased delay
            Assert.True(requestCountPolicy.RequestCount < exponentialRequestCount);
        }
    }
}
