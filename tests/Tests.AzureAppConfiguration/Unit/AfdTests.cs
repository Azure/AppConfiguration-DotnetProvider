// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Azure;
using System;
using System.Linq;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class AfdTests
    {
        private class TestClientFactory : IAzureClientFactory<ConfigurationClient>
        {
            public ConfigurationClient CreateClient(string name)
            {
                throw new NotImplementedException();
            }
        }

        [Fact]
        public void AfdTests_ConnectThrowsAfterConnectAzureFrontDoor()
        {
            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            var endpoint = new Uri("https://fake-endpoint.azconfig.io");
            var connectionString = "Endpoint=https://fake-endpoint.azconfig.io;Id=test;Secret=123456";
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.Connect(endpoint, new DefaultAzureCredential());
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.ConnectionConflict, exception.InnerException.Message);

            exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.Connect(connectionString);
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.ConnectionConflict, exception.InnerException.Message);
        }

        [Fact]
        public void AfdTests_ConnectAzureFrontDoorThrowsAfterConnect()
        {
            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            var endpoint = new Uri("https://fake-endpoint.azconfig.io");
            var connectionString = "Endpoint=https://fake-endpoint.azconfig.io;Id=test;Secret=123456";
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.Connect(endpoint, new DefaultAzureCredential());
                    options.ConnectAzureFrontDoor(afdEndpoint);
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.ConnectionConflict, exception.InnerException.Message);

            exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.Connect(connectionString);
                    options.ConnectAzureFrontDoor(afdEndpoint);
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.ConnectionConflict, exception.InnerException.Message);
        }

        [Fact]
        public void AfdTests_LoadbalancingIsUnsupportedWhenConnectAzureFrontDoor()
        {
            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.LoadBalancingEnabled = true;
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.AfdLoadBalancingUnsupported, exception.InnerException.Message);
        }

        [Fact]
        public void AfdTests_CustomClientOptionsIsUnsupportedWhenConnectAzureFrontDoor()
        {
            var afdEndpoint = new Uri("https://test.b01.azurefd.net");
            var builder = new ConfigurationBuilder();
            var exception = Record.Exception(() =>
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    options.ConnectAzureFrontDoor(afdEndpoint);
                    options.SetClientFactory(new TestClientFactory());
                });
                builder.Build();
            });
            Assert.NotNull(exception);
            Assert.IsType<ArgumentException>(exception);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(ErrorMessages.AfdCustomClientFactoryUnsupported, exception.InnerException.Message);
        }

        [Fact]
        public void AfdTests_LoadsConfiguration()
        {
        }
    }
}
