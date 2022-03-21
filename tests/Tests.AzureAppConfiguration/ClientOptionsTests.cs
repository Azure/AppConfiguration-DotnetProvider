using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.ConfigurationClients;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
            var mockClient = new Mock<IConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.FromResult(Enumerable.Empty<ConfigurationSetting>()));

            var configurationClientFactory = new MockedConfigurationClientFactory(mockClient);
            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect("test_connection_string");
                }, configurationClientFactory);

            // Act
            configBuilder.Build();

            int defaultMaxRetries = configurationClientFactory.ClientOptions.Retry.MaxRetries;
            TimeSpan defaultMaxRetryDelay = configurationClientFactory.ClientOptions.Retry.MaxDelay;

            // Arrange
            int maxRetries = defaultMaxRetries + 1;
            mockClient.Invocations.Clear();
            configurationClientFactory = new MockedConfigurationClientFactory(mockClient);

            configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect("test_connection_string");
                    options.ConfigureClientOptions(clientOptions => clientOptions.Retry.MaxRetries = maxRetries);
                }, configurationClientFactory);

            // Act
            configBuilder.Build();

            // Assert
            mockClient.Verify(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(1, configurationClientFactory.ConnectionStringCallCount);
            Assert.Equal(maxRetries, configurationClientFactory.ClientOptions.Retry.MaxRetries);
            Assert.Equal(defaultMaxRetryDelay, configurationClientFactory.ClientOptions.Retry.MaxDelay);
        }
    }
}
