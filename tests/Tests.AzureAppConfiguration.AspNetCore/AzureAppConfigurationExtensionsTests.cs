using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using Xunit;

namespace Tests.AzureAppConfiguration.AspNetCore
{
    public class AzureAppConfigurationExtensionsTests
    {
        [Fact]
        public void UseAzureAppConfiguration_ThrowsOnMissingAddAzureAppConfiguration()
        {
            // Arrange
            var services = new ServiceCollection();

            var mockApplicationBuilder = new Mock<IApplicationBuilder>(MockBehavior.Strict);
            mockApplicationBuilder.SetupGet(builder => builder.ApplicationServices).Returns(services.BuildServiceProvider());
            void action() => mockApplicationBuilder.Object.UseAzureAppConfiguration();

            // Act and Assert
            var exception = Assert.Throws<InvalidOperationException>(action);
            Assert.Contains("AddAzureAppConfiguration", exception.Message);
        }
    }
}
