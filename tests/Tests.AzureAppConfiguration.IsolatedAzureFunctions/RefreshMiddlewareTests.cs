// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Azure.AppConfiguration.IsolatedAzureFunctions;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System.Linq;
using Xunit;

namespace Tests.AzureAppConfiguration.IsolatedAzureFunctions
{
    public class RefreshMiddlewareTests
    {
        [Fact]
        public void RefreshMiddlewareTests_MiddlewareConstructorRetrievesIConfigurationRefresher()
        {
            // Arrange
            IConfigurationRefresher[] refreshers = { new AzureAppConfigurationRefresher() };
            var mockRefresherProvider = new Mock<IConfigurationRefresherProvider>(MockBehavior.Strict);
            mockRefresherProvider.SetupGet(provider => provider.Refreshers).Returns(refreshers);

            // Act
            var middleware = new AzureAppConfigurationRefreshMiddleware(mockRefresherProvider.Object);

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

            // Act
            var middleware = new AzureAppConfigurationRefreshMiddleware(mockRefresherProvider.Object);

            // Assert
            Assert.NotNull(middleware.Refreshers);
            Assert.Equal(2, middleware.Refreshers.Count());
        }
    }
}
