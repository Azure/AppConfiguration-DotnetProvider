// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
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

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConfigureStartupOptions(startupOptions =>
                    {
                        startupOptions.Timeout = TimeSpan.FromSeconds(15);
                    });
                    options.Connect(TestHelpers.CreateMockEndpointString());
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                });

            // Act - Build
            Exception exception = Assert.Throws<TimeoutException>(() => configBuilder.Build());

            // Assert the inner aggregate exception
            Assert.IsType<AggregateException>(exception.InnerException);

            // Assert the second inner aggregate exception
            Assert.IsType<AggregateException>(exception.InnerException.InnerException);

            var exponentialRequestCount = requestCountPolicy.RequestCount;

            requestCountPolicy.ResetRequestCount();

            configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConfigureStartupOptions(startupOptions =>
                    {
                        startupOptions.Timeout = TimeSpan.FromSeconds(15);
                    });
                    options.Connect(TestHelpers.CreateMockEndpointString());
                    options.ConfigureClientOptions(clientOptions => clientOptions.Retry.Delay = TimeSpan.FromSeconds(60));
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                });

            // Act - Build
            exception = Assert.Throws<TimeoutException>(() => configBuilder.Build());

            // Assert the inner aggregate exception
            Assert.IsType<AggregateException>(exception.InnerException);

            // Assert the inner request failed exceptions
            Assert.True((exception.InnerException as AggregateException)?.InnerExceptions?.All(e => e is RequestFailedException) ?? false);

            // Assert less retries due to increased delay
            Assert.True(requestCountPolicy.RequestCount < exponentialRequestCount);
        }
    }
}
