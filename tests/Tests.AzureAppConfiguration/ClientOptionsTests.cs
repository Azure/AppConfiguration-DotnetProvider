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

            // Calculate expected number of retries based on options.Startup.Timeout
            Assert.Equal((int)Math.Floor(Math.Log(startupTimeout, 2)) + 1, requestCountPolicy.RequestCount);

            requestCountPolicy.ResetRequestCount();

            var defaultDelay = 0.0;

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

            // Calculate expected number of retries based on options.Startup.Timeout with RetryMode.Fixed
            Assert.Equal((int)Math.Floor(startupTimeout / defaultDelay) + 1, requestCountPolicy.RequestCount);
        }
    }
}
