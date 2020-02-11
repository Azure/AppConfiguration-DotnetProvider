using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class ClientOptionsTests
    {
        [Fact]
        public void ClientOptionsTests_OverridesDefaultClientOptions()
        {
            // Arrange
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                    .Returns(new MockAsyncPageable(new List<ConfigurationSetting>()));

            var configurationClientFactory = new MockedConfigurationClientFactory(mockClient);
            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect("test_connection_string");
                }, configurationClientFactory);

            // Act
            configBuilder.Build();

            // Assert
            mockClient.Verify(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(1, configurationClientFactory.ConnectionStringCallCount);
            Assert.Equal(AzureAppConfigurationOptions.MaxRetries, configurationClientFactory.ClientOptions.Retry.MaxRetries);
            Assert.Equal(AzureAppConfigurationOptions.MaxRetryDelay, configurationClientFactory.ClientOptions.Retry.MaxDelay);

            // Arrange
            int maxRetries = 5;
            mockClient.ResetCalls();
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
            Assert.Equal(AzureAppConfigurationOptions.MaxRetryDelay, configurationClientFactory.ClientOptions.Retry.MaxDelay);
        }
    }
}
