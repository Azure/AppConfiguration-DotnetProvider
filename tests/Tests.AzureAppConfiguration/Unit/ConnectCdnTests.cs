using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class ConnectCdnTests
    {
        [Fact]
        public void ConnectCdnTests_CdnWithClientFactoryRequiresClientOptions()
        {
            var mockClientFactory = new Mock<IAzureClientFactory<ConfigurationClient>>();

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.SetClientFactory(mockClientFactory.Object) // No client options provided
                           .ConnectCdn(TestHelpers.MockCdnEndpoint);
                });

            Exception exception = Assert.Throws<ArgumentException>(() => configBuilder.Build());
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        [Fact]
        public void ConnectCdnTests_CdnWithClientFactoryAndClientOptionsSucceeds()
        {
            var mockClientFactory = new Mock<IAzureClientFactory<ConfigurationClient>>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var clientOptions = new ConfigurationClientOptions();

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting>()));

            mockClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(mockClient.Object);

            AzureAppConfigurationOptions capturedOptions = null;

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.SetClientFactory(mockClientFactory.Object, clientOptions) // Client options provided
                           .ConnectCdn(TestHelpers.MockCdnEndpoint);
                    capturedOptions = options;
                });

            Assert.NotNull(configBuilder.Build());

            Assert.NotNull(capturedOptions);
            Assert.True(capturedOptions.IsCdnEnabled);
            Assert.Equal(clientOptions, capturedOptions.ClientOptions);

            mockClient.Verify(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void ConnectCdnTests_DoesNotSupportLoadBalancing()
        {
            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ConnectCdn(TestHelpers.MockCdnEndpoint)
                           .LoadBalancingEnabled = true;
                });

            Exception exception = Assert.Throws<ArgumentException>(() => configBuilder.Build());
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }
    }
}