// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Tests.AzureAppConfiguration.Functions.Worker
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
