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
            int startupTimeout = 10;

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(TestHelpers.CreateMockEndpointString());
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                    options.Startup.Timeout = TimeSpan.FromSeconds(startupTimeout);
                });

            // Act - Build
            Assert.Throws<TaskCanceledException>(configBuilder.Build);

            // Assert the connect call made requests to the configuration store.
            Assert.True(requestCountPolicy.RequestCount > 1);

            var exponentialRequestCount = requestCountPolicy.RequestCount;
            requestCountPolicy.ResetRequestCount();

            configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(TestHelpers.CreateMockEndpointString());
                    options.ConfigureClientOptions(clientOptions => clientOptions.Retry.Mode = RetryMode.Fixed);
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                    options.Startup.Timeout = TimeSpan.FromSeconds(startupTimeout);
                });

            // Act - Build
            Assert.Throws<TaskCanceledException>(configBuilder.Build);

            // Assert the connect call made requests to the configuration store, more than the exponential mode.
            Assert.True(requestCountPolicy.RequestCount > exponentialRequestCount);
        }
    }
}
