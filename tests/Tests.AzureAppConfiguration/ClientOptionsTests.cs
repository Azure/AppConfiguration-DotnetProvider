﻿// Copyright (c) Microsoft Corporation.
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
            TimeSpan startupTimeout = TimeSpan.FromSeconds(5);

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(TestHelpers.CreateMockEndpointString());
                    options.Startup.Timeout = startupTimeout;
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                    defaultMaxRetries = options.ClientOptions.Retry.MaxRetries;
                });

            try
            {
                configBuilder.Build();
            }
            catch (TaskCanceledException tce)
            {
            }

            // Act - Build
            Assert.Throws<AggregateException>(configBuilder.Build);

            // Assert defaultMaxRetries + 1 original request = totalRequestCount
            Assert.Equal(defaultMaxRetries + 1, requestCountPolicy.RequestCount);

            requestCountPolicy.ResetRequestCount();

            // Arrange
            int maxRetries = defaultMaxRetries + 1;

            configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(TestHelpers.CreateMockEndpointString());
                    options.Startup.Timeout = startupTimeout;
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
