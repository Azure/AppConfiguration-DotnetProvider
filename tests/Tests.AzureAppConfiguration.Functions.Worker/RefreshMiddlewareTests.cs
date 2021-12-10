// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Azure.AppConfiguration.Functions.Worker;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration.Functions.Worker
{
    public class RefreshMiddlewareTests
    {
        [Fact]
        public void RefreshMiddlewareTests_MiddlewareConstructorRetrievesIConfigurationRefresher()
        {
            // Arrange
            var mockRefresher = new Mock<IConfigurationRefresher>(MockBehavior.Strict);
            mockRefresher.Setup(provider => provider.TryRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var mockRefresherProvider = new Mock<IConfigurationRefresherProvider>(MockBehavior.Strict);
            mockRefresherProvider.SetupGet(provider => provider.Refreshers).Returns(new IConfigurationRefresher[] { mockRefresher.Object });

            var mockContext = new Mock<FunctionContext>();
            var mockFunctionExecutionDelegate = new Mock<FunctionExecutionDelegate>();

            // Act
            var middleware = new AzureAppConfigurationRefreshMiddleware(mockRefresherProvider.Object);
            _ = middleware.Invoke(mockContext.Object, mockFunctionExecutionDelegate.Object);

            // Assert
            mockRefresher.Verify(refresher => refresher.TryRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void RefreshMiddlewareTests_MiddlewareConstructorRetrievesMultipleIConfigurationRefreshers()
        {
            // Arrange
            var mockRefresher = new Mock<IConfigurationRefresher>(MockBehavior.Strict);
            mockRefresher.Setup(provider => provider.TryRefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var mockRefresherProvider = new Mock<IConfigurationRefresherProvider>(MockBehavior.Strict);
            mockRefresherProvider.SetupGet(provider => provider.Refreshers).Returns(new IConfigurationRefresher[] { mockRefresher.Object, mockRefresher.Object });

            var mockContext = new Mock<FunctionContext>();
            var mockFunctionExecutionDelegate = new Mock<FunctionExecutionDelegate>();

            // Act
            var middleware = new AzureAppConfigurationRefreshMiddleware(mockRefresherProvider.Object);
            _ = middleware.Invoke(mockContext.Object, mockFunctionExecutionDelegate.Object);

            // Assert
            mockRefresher.Verify(refresher => refresher.TryRefreshAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}
