// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.AppConfiguration.AspNetCore;
using Microsoft.Extensions.Configuration;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using static Tests.AzureAppConfiguration.AspNetCore.TestHelper;

namespace Tests.AzureAppConfiguration.AspNetCore
{
    public class RefreshMiddlewareTests
    {
        List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey1",
                label: "label",
                value: "TestValue1",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey2",
                label: "label",
                value: "TestValue2",
                eTag: new ETag("31c38369-831f-4bf1-b9ad-79db56c8b989"),
                contentType: "text"),

            ConfigurationModelFactory.ConfigurationSetting(
                key: "TestKey3",
                label: "label",
                value: "TestValue3",
                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text")
        };

        [Fact]
        public void RefreshMiddlewareTests_MiddlewareConstructorParsesIConfigurationRefresher()
        {
            // Arrange
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection));

            var delegateMock = new Mock<RequestDelegate>();
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.Client = mockClient.Object)
                .Build();

            // Act
            var middleware = new AzureAppConfigurationRefreshMiddleware(delegateMock.Object, configuration);

            // Assert
            Assert.NotNull(middleware.Refreshers);
            Assert.Equal(1, middleware.Refreshers.Count);
        }

        [Fact]
        public void RefreshMiddlewareTests_MiddlewareConstructorParsesMultipleIConfigurationRefreshers()
        {
            // Arrange
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollection));

            var delegateMock = new Mock<RequestDelegate>();
            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options => options.Client = mockClient.Object)
                .AddAzureAppConfiguration(options => options.Client = mockClient.Object)
                .Build();

            // Act
            var middleware = new AzureAppConfigurationRefreshMiddleware(delegateMock.Object, configuration);

            // Assert
            Assert.NotNull(middleware.Refreshers);
            Assert.Equal(2, middleware.Refreshers.Count);
        }

        [Fact]
        public void RefreshMiddlewareTests_InvalidOperationExceptionOnIConfigurationCastFailure()
        {
            // Arrange
            var delegateMock = new Mock<RequestDelegate>();
            var configMock = new Mock<IConfiguration>();
            Action action = () => new AzureAppConfigurationRefreshMiddleware(delegateMock.Object, configMock.Object);

            // Act and Assert
            Assert.Throws<InvalidOperationException>(action);
        }
    }
}
