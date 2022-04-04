// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System;
using System.Linq;
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
            var mockClient = new Mock<FailOverClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(Enumerable.Empty<ConfigurationSetting>()));

            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
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
    }
}
