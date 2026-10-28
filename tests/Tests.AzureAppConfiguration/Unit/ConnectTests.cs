// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
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
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ConnectTests_UsesFeatureFlagClientFactoryIfSpecified(bool useConnectionString)
        {
            var mockConfigurationClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockConfigurationClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting>()));

            var mockFeatureFlagClient = new Mock<FeatureFlagClient>(MockBehavior.Strict);
            var configurationClientFactory = new Mock<IAzureClientFactory<ConfigurationClient>>(MockBehavior.Strict);
            var featureFlagClientFactory = new Mock<IAzureClientFactory<FeatureFlagClient>>(MockBehavior.Strict);

            configurationClientFactory
                .Setup(factory => factory.CreateClient(TestHelpers.PrimaryConfigStoreEndpoint.AbsoluteUri))
                .Returns(mockConfigurationClient.Object);

            featureFlagClientFactory
                .Setup(factory => factory.CreateClient(TestHelpers.PrimaryConfigStoreEndpoint.AbsoluteUri))
                .Returns(mockFeatureFlagClient.Object);

            var credential = new Mock<TokenCredential>(MockBehavior.Strict);

            new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ReplicaDiscoveryEnabled = false;

                    if (useConnectionString)
                    {
                        options.Connect(TestHelpers.CreateMockEndpointString());
                    }
                    else
                    {
                        options.Connect(TestHelpers.PrimaryConfigStoreEndpoint, credential.Object);
                    }

                    options.SetClientFactory(configurationClientFactory.Object);
                    options.SetFeatureFlagClientFactory(featureFlagClientFactory.Object);
                })
                .Build();

            featureFlagClientFactory.Verify(
                factory => factory.CreateClient(TestHelpers.PrimaryConfigStoreEndpoint.AbsoluteUri),
                Times.Once);
        }

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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
            var requestCountPolicy = new HttpRequestCountPipelinePolicy();
            int defaultMaxRetries = 0;

            configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConfigureStartupOptions(startupOptions =>
                    {
                        startupOptions.Timeout = TimeSpan.FromSeconds(15);
                    });
                    options.Connect("invalid_connection_string");
                    options.Connect(TestHelpers.PrimaryConfigStoreEndpoint, mockTokenCredential.Object);
                    options.ClientOptions.AddPolicy(requestCountPolicy, HttpPipelinePosition.PerRetry);
                    defaultMaxRetries = options.ClientOptions.Retry.MaxRetries;
                });

            // Act - Build
            Exception exception = Assert.Throws<TimeoutException>(() => configBuilder.Build());

            // Assert the inner aggregate exception
            Assert.IsType<AggregateException>(exception.InnerException);

            // Assert the second inner aggregate exception
            Assert.IsType<AggregateException>(exception.InnerException.InnerException);

            // Assert the second connect call was successful and it made requests to the configuration store.
            Assert.True(requestCountPolicy.RequestCount >= defaultMaxRetries + 1);
        }
    }
}
