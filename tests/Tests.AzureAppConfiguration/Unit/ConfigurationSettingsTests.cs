// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using System;
using System.Collections.Generic;
using Xunit;

#pragma warning disable SCME0002 // Experimental API

namespace Tests.AzureAppConfiguration
{
    public class ConfigurationSettingsTests
    {
        private const string SectionName = "AppConfiguration";
        private const string TestEndpoint = "https://azure.azconfig.io";

        private static Dictionary<string, string> CreateConfigSection()
        {
            return new Dictionary<string, string>
            {
                { $"{SectionName}:Endpoint", TestEndpoint },
                { $"{SectionName}:Credential:CredentialSource", "AzureCli" }
            };
        }

        [Fact]
        public void AddAppConfigurationsThrowsOnNullBuilder()
        {
            Assert.Throws<ArgumentNullException>(() =>
                AzureAppConfigurationExtensions.AddAppConfigurations(null, SectionName));
        }

        [Fact]
        public void AddAppConfigurationsThrowsOnNullSectionName()
        {
            var builder = new ConfigurationBuilder();
            Assert.Throws<ArgumentException>(() =>
                builder.AddAppConfigurations(null));
        }

        [Fact]
        public void AddAppConfigurationsThrowsOnEmptySectionName()
        {
            var builder = new ConfigurationBuilder();
            Assert.Throws<ArgumentException>(() =>
                builder.AddAppConfigurations(string.Empty));
        }

        [Fact]
        public void AddAppConfigurationsWithActionThrowsOnNullBuilder()
        {
            Assert.Throws<ArgumentNullException>(() =>
                AzureAppConfigurationExtensions.AddAppConfigurations(null, SectionName, options => { }));
        }

        [Fact]
        public void AddAppConfigurationsWithActionThrowsOnNullSectionName()
        {
            var builder = new ConfigurationBuilder();
            Assert.Throws<ArgumentException>(() =>
                builder.AddAppConfigurations(null, options => { }));
        }

        [Fact]
        public void AddAppConfigurationsWithActionThrowsOnEmptySectionName()
        {
            var builder = new ConfigurationBuilder();
            Assert.Throws<ArgumentException>(() =>
                builder.AddAppConfigurations(string.Empty, options => { }));
        }

        [Fact]
        public void AddAppConfigurationsAddsSourceToBuilder()
        {
            // Arrange
            int initialSourceCount = 1; // the in-memory source

            IConfigurationBuilder builder = new ConfigurationBuilder()
                .AddInMemoryCollection(CreateConfigSection());

            // Act
            builder.AddAppConfigurations(SectionName);

            // Assert - A new source should have been added beyond the initial in-memory source
            Assert.Equal(initialSourceCount + 1, builder.Sources.Count);
        }

        [Fact]
        public void AddAppConfigurationsWithActionAddsSourceToBuilder()
        {
            // Arrange
            int initialSourceCount = 1;

            IConfigurationBuilder builder = new ConfigurationBuilder()
                .AddInMemoryCollection(CreateConfigSection());

            // Act
            builder.AddAppConfigurations(SectionName, options => { });

            // Assert
            Assert.Equal(initialSourceCount + 1, builder.Sources.Count);
        }

        [Fact]
        public void AddAppConfigurationsOptionalBuildSucceeds()
        {
            // Arrange
            IConfigurationBuilder builder = new ConfigurationBuilder()
                .AddInMemoryCollection(CreateConfigSection())
                .AddAppConfigurations(SectionName, options =>
                {
                    options.ConfigureStartupOptions(startupOptions =>
                    {
                        startupOptions.Timeout = TimeSpan.FromMilliseconds(1);
                    });
                }, optional: true);

            // Act - Build should succeed because optional=true suppresses load errors
            IConfigurationRoot config = builder.Build();

            // Assert
            Assert.NotNull(config);
        }

        [Fact]
        public void AddAppConfigurationsWithActionInvokesCallback()
        {
            // Arrange
            bool actionInvoked = false;

            IConfigurationBuilder builder = new ConfigurationBuilder()
                .AddInMemoryCollection(CreateConfigSection())
                .AddAppConfigurations(SectionName, options =>
                {
                    actionInvoked = true;
                    options.ConfigureStartupOptions(startupOptions =>
                    {
                        startupOptions.Timeout = TimeSpan.FromMilliseconds(1);
                    });
                }, optional: true);

            // Act
            builder.Build();

            // Assert
            Assert.True(actionInvoked);
        }
    }
}
