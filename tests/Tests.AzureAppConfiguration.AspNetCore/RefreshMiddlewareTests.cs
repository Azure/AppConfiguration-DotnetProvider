// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.AppConfiguration.AspNetCore;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using static Tests.AzureAppConfiguration.AspNetCore.TestHelper;

namespace Tests.AzureAppConfiguration.AspNetCore
{
    public class RefreshMiddlewareTests
    {
        private readonly List<ConfigurationSetting> _kvCollection = new List<ConfigurationSetting>
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
        public void RefreshMiddlewareTests_MiddlewareConstructorRetrievesIConfigurationRefresher()
        {
            // Arrange
            IConfigurationRefresher[] refreshers = { new AzureAppConfigurationRefresher() };
            var mockRefresherProvider = new Mock<IConfigurationRefresherProvider>(MockBehavior.Strict);
            mockRefresherProvider.SetupGet(provider => provider.Refreshers).Returns(refreshers);
            var delegateMock = new Mock<RequestDelegate>();

            // Act
            var middleware = new AzureAppConfigurationRefreshMiddleware(delegateMock.Object, mockRefresherProvider.Object);

            // Assert
            Assert.NotNull(middleware.Refreshers);
            Assert.Single(middleware.Refreshers);
        }

        [Fact]
        public void RefreshMiddlewareTests_MiddlewareConstructorRetrievesMultipleIConfigurationRefreshers()
        {
            // Arrange
            IConfigurationRefresher[] refreshers = { new AzureAppConfigurationRefresher(), new AzureAppConfigurationRefresher() };
            var mockRefresherProvider = new Mock<IConfigurationRefresherProvider>(MockBehavior.Strict);
            mockRefresherProvider.SetupGet(provider => provider.Refreshers).Returns(refreshers);
            var delegateMock = new Mock<RequestDelegate>();

            // Act
            var middleware = new AzureAppConfigurationRefreshMiddleware(delegateMock.Object, mockRefresherProvider.Object);

            // Assert
            Assert.NotNull(middleware.Refreshers);
            Assert.Equal(2, middleware.Refreshers.Count());
        }
    }
}
