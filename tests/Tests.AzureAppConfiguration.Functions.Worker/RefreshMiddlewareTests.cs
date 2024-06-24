// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.AppConfiguration.Functions.Worker;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Moq;
using Xunit;

namespace Tests.AzureAppConfiguration.Functions.Worker
{
    public class RefreshMiddlewareTests
    {
        [Fact]
        public async Task RefreshMiddlewareTests_MiddlewareConstructorRetrievesIConfigurationRefresher()
        {
            // Arrange
            var mockRefresher = new Mock<IConfigurationRefresher>(MockBehavior.Strict);

            int callCount = 0;

            mockRefresher.Setup(provider => provider.TryRefreshAsync(It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref callCount))
                .ReturnsAsync(true);

            var mockRefresherProvider = new Mock<IConfigurationRefresherProvider>(MockBehavior.Strict);
            mockRefresherProvider.SetupGet(provider => provider.Refreshers).Returns(new IConfigurationRefresher[] { mockRefresher.Object });

            var mockContext = new Mock<FunctionContext>();
            var mockFunctionExecutionDelegate = new Mock<FunctionExecutionDelegate>();

            // Act
            var middleware = new AzureAppConfigurationRefreshMiddleware(mockRefresherProvider.Object);
            _ = middleware.Invoke(mockContext.Object, mockFunctionExecutionDelegate.Object);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (callCount < 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            // Assert
            mockRefresher.Verify(refresher => refresher.TryRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RefreshMiddlewareTests_MiddlewareConstructorRetrievesMultipleIConfigurationRefreshers()
        {
            // Arrange
            var mockRefresher = new Mock<IConfigurationRefresher>(MockBehavior.Strict);

            int callCount = 0;

            mockRefresher.Setup(provider => provider.TryRefreshAsync(It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref callCount))
                .ReturnsAsync(true);

            var mockRefresherProvider = new Mock<IConfigurationRefresherProvider>(MockBehavior.Strict);
            mockRefresherProvider.SetupGet(provider => provider.Refreshers).Returns(new IConfigurationRefresher[] { mockRefresher.Object, mockRefresher.Object });

            var mockContext = new Mock<FunctionContext>();
            var mockFunctionExecutionDelegate = new Mock<FunctionExecutionDelegate>();

            // Act
            var middleware = new AzureAppConfigurationRefreshMiddleware(mockRefresherProvider.Object);
            _ = middleware.Invoke(mockContext.Object, mockFunctionExecutionDelegate.Object);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (callCount < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            // Assert
            mockRefresher.Verify(refresher => refresher.TryRefreshAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}
