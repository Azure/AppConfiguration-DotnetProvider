// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class ConnectTests
    {
        [Fact]
        public void ConnectTests_UsesClientInstanceIfSpecified()
        {
            // Arrange
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting>()));

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
                });

            // Act
            configBuilder.Build();

            // Assert
            mockClient.Verify(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void ConnectTests_ThrowsIfConnectNotInvoked()
        {
            // Arrange
            var configBuilder = new ConfigurationBuilder().AddAzureAppConfiguration(options => options.Select("TestKey1"));
            Action action = () => configBuilder.Build();

            // Act and Assert
            Assert.Throws<ArgumentException>(action);
        }


        [Fact]
        public void ConnectTests_UsesParametersFromLatestConnectCall()
        {
            // Arrange
            var mockTokenCredential = new Mock<TokenCredential>();
            mockTokenCredential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                        .Returns(new ValueTask<AccessToken>(new AccessToken("", DateTimeOffset.Now.AddDays(2))));

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(TestHelpers.PrimaryConfigStoreEndpoint, mockTokenCredential.Object);
                    options.Connect("Invalid connection string");
                });

            // Act and assert. (latest connect call should throw Argument exception)
            Assert.Throws<ArgumentException>(configBuilder.Build);

            // Arrange
            MockTransport mockTransport = new MockTransport(new MockResponse(429), new MockResponse(429), new MockResponse(429));
            var requestCountPolicy = new HttpRequestCountPipelinePolicy();
            int defaultMaxRetries = 0;

            configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect("invalid_connection_string");
                    options.Connect(TestHelpers.PrimaryConfigStoreEndpoint, mockTokenCredential.Object);
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                    defaultMaxRetries = options.ClientOptions.Retry.MaxRetries;
                });

            // Act
            Assert.Throws<AggregateException>(configBuilder.Build);

            // Assert the second connect call was successful and it made requests to the configuration store.
            Assert.Equal(defaultMaxRetries + 1, requestCountPolicy.RequestCount);
        }
    }
}
