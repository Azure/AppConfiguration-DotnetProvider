// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.AzureAppConfiguration
{
    public class HealthCheckTest
    {
        readonly List<ConfigurationSetting> kvCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting("TestKey1", "TestValue1", "label",
                eTag: new ETag("0a76e3d7-7ec1-4e37-883c-9ea6d0d89e63"),
                contentType:"text"),
            ConfigurationModelFactory.ConfigurationSetting("TestKey2", "TestValue2", "label",
                eTag: new ETag("31c38369-831f-4bf1-b9ad-79db56c8b989"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("TestKey3", "TestValue3", "label",

                eTag: new ETag("bb203f2b-c113-44fc-995d-b933c2143339"),
                contentType: "text"),
            ConfigurationModelFactory.ConfigurationSetting("TestKey4", "TestValue4", "label",
                eTag: new ETag("3ca43b3e-d544-4b0c-b3a2-e7a7284217a2"),
                contentType: "text"),
        };

        [Fact]
        public async Task HealthCheckTests_ReturnsHealthyWhenInitialLoadIsCompleted()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(kvCollection));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            IHealthCheck healthCheck = new AzureAppConfigurationHealthCheck(config);

            Assert.True(config["TestKey1"] == "TestValue1");
            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        [Fact]
        public async Task HealthCheckTests_ReturnsUnhealthyWhenRefreshFailed()
        {
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(kvCollection))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));

            mockClient.SetupSequence(c => c.CheckConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()))
                       .Returns(new MockAsyncPageable(Enumerable.Empty<ConfigurationSetting>().ToList()));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.MinBackoffDuration = TimeSpan.FromSeconds(2);
                    options.ConfigurationSettingPageIterator = new MockConfigurationSettingPageIterator();
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.RegisterAll()
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            IHealthCheck healthCheck = new AzureAppConfigurationHealthCheck(config);

            var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, result.Status);

            // Wait for the refresh interval to expire
            Thread.Sleep(1000);

            await refresher.TryRefreshAsync();
            result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, result.Status);

            // Wait for client backoff to end
            Thread.Sleep(3000);

            await refresher.RefreshAsync();
            result = await healthCheck.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        [Fact]
        public async Task HealthCheckTests_RegisterAzureAppConfigurationHealthCheck()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(kvCollection));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging(); // add logging for health check service
            services.AddHealthChecks()
                .AddAzureAppConfiguration();
            var provider = services.BuildServiceProvider();
            var healthCheckService = provider.GetRequiredService<HealthCheckService>();

            var result = await healthCheckService.CheckHealthAsync();
            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Contains(HealthCheckConstants.HealthCheckRegistrationName, result.Entries.Keys);
            Assert.Equal(HealthStatus.Healthy, result.Entries[HealthCheckConstants.HealthCheckRegistrationName].Status);
        }

        [Fact]
        public async Task HealthCheckTests_ShouldRespectHealthCheckRegistration()
        {
            IConfigurationRefresher refresher = null;
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            mockClient.SetupSequence(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Returns(new MockAsyncPageable(kvCollection));

            mockClient.SetupSequence(c => c.CheckConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                       .Throws(new RequestFailedException(503, "Request failed."));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.MinBackoffDuration = TimeSpan.FromSeconds(2);
                    options.ConfigurationSettingPageIterator = new MockConfigurationSettingPageIterator();
                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.RegisterAll()
                            .SetRefreshInterval(TimeSpan.FromSeconds(1));
                    });
                    refresher = options.GetRefresher();
                })
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging(); // add logging for health check service
            services.AddHealthChecks()
                .AddAzureAppConfiguration(
                    name: "TestName",
                    failureStatus: HealthStatus.Degraded);
            var provider = services.BuildServiceProvider();
            var healthCheckService = provider.GetRequiredService<HealthCheckService>();

            var result = await healthCheckService.CheckHealthAsync();
            Assert.Equal(HealthStatus.Healthy, result.Status);

            // Wait for the refresh interval to expire
            Thread.Sleep(2000);

            await refresher.TryRefreshAsync();
            result = await healthCheckService.CheckHealthAsync();
            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains("TestName", result.Entries.Keys);
            Assert.Equal(HealthStatus.Degraded, result.Entries["TestName"].Status);
        }
    }
}
