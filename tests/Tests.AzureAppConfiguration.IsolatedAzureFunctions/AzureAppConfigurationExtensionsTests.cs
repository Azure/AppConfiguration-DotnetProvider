// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Azure.AppConfiguration.IsolatedAzureFunctions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using Xunit;

namespace Tests.AzureAppConfiguration.IsolatedAzureFunctions
{
    public class AzureAppConfigurationExtensionsTests
    {
        [Fact]
        public void UseAzureAppConfiguration_ThrowsOnMissingAddAzureAppConfiguration()
        {
            // Arrange
            var services = new ServiceCollection();

            var mockApplicationBuilder = new Mock<IFunctionsWorkerApplicationBuilder>(MockBehavior.Strict);
            mockApplicationBuilder.SetupGet(builder => builder.Services).Returns(services);
            void action() => mockApplicationBuilder.Object.UseAzureAppConfiguration();

            // Act and Assert
            var exception = Assert.Throws<InvalidOperationException>(action);
            Assert.Contains("AddAzureAppConfiguration", exception.Message);
        }
    }
}
