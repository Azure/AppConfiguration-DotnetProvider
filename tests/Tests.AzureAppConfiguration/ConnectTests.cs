﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
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
    public class ConnectTests
    {
        [Fact]
        public void ConnectTests_UsesClientInstanceIfSpecified()
        {
            // Arrange
            var mockClient = new Mock<IConfigurationClient>(MockBehavior.Strict);
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
        public void ConnectTests_UsesParametersFromLatestConnectCall()
        {
            // Arrange
            var mockTokenCredential = new Mock<TokenCredential>();
            mockTokenCredential.Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<AccessToken>(new AccessToken("", DateTimeOffset.Now.AddDays(2))));

            var mockClient = new Mock<IConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.FromResult(Enumerable.Empty<ConfigurationSetting>()));

            var configurationClientFactory = new MockedConfigurationClientFactory(mockClient);
            var configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect(new List<Uri>() { new Uri("https://test") }, mockTokenCredential.Object);
                    options.Connect("invalid_connection_string");
                }, configurationClientFactory);

            // Act
            configBuilder.Build();

            // Assert
            mockClient.Verify(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(1, configurationClientFactory.ConnectionStringCallCount);
            Assert.Equal(0, configurationClientFactory.TokenCredentialCallCount);

            // Arrange
            mockClient.Invocations.Clear();
            configurationClientFactory = new MockedConfigurationClientFactory(mockClient);
            configBuilder = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Connect("invalid_connection_string");
                    options.Connect(new List<Uri>() { new Uri("https://test") }, mockTokenCredential.Object);
                }, configurationClientFactory);

            // Act
            configBuilder.Build();

            // Assert
            mockClient.Verify(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(0, configurationClientFactory.ConnectionStringCallCount);
            Assert.Equal(1, configurationClientFactory.TokenCredentialCallCount);
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
