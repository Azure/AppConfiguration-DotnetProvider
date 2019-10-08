using Azure;
using Azure.Core.Http;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class KeyVaultReferenceTests
    {

        ConfigurationSetting _kv = ConfigurationModelFactory.ConfigurationSetting(
            key: "TestKey1",
            value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
            contentType: KeyVaultConstants.ContentType + "; charset=utf-8");

        ConfigurationSetting _kvNoUrl = ConfigurationModelFactory.ConfigurationSetting(
            key: "TestKey1",
            value: "Test",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
            contentType: KeyVaultConstants.ContentType + "; charset=utf-8");

        ConfigurationSetting _kvWrongContentType = ConfigurationModelFactory.ConfigurationSetting(
            key: "TestKey1",
            value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }",
            eTag: new ETag("c3c231fd -39a0-4cb6-3237-4614474b92c1"),
            contentType: "test");

        List<ConfigurationSetting> _kvCollectionPageOne = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key:"TK1",
                value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }
                   ",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8"),

            ConfigurationModelFactory.ConfigurationSetting(
                key:"TK2",
                value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/Password3/6db5a48680104dda9097b1e6d859e553""
                    }
                   ",
                eTag : new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8"),
        };

        [Fact]
        public void NotSecretIdentifierURI()
        {
            string secretValue = "SecretValue from KeyVault";

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kvNoUrl }));

            var builder = new ConfigurationBuilder();

            var options = new AzureAppConfigurationOptions()
            {
                Client = mockClient.Object
            };

            IConfiguration config = null;

            var exception = Record.Exception(() =>
            {
                options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue));
                builder.AddAzureAppConfiguration(options);
                builder.Build();
            });

            Assert.IsType<KeyVaultReferenceException>(exception);
            Assert.Null(config);
        }

        [Fact]
        public void UseSecret()
        {
            string secretValue = "SecretValue from KeyVault";

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var builder = new ConfigurationBuilder();

            var options = new AzureAppConfigurationOptions()
            {
                Client = mockClient.Object
            };

            options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue));

            builder.AddAzureAppConfiguration(options);

            var config = builder.Build();

            Assert.Equal(secretValue, config[_kv.Key]);
        }

        [Fact]
        public void DisabledSecretIdentifier()
        {
            string secretValue = "SecretValue from KeyVault";

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var builder = new ConfigurationBuilder();

            var options = new AzureAppConfigurationOptions()
            {
                Client = mockClient.Object
            };

            KeyVaultReferenceException ex = Assert.Throws<KeyVaultReferenceException>(() =>
            {
                options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue) { IsEnabled = false });
                builder.AddAzureAppConfiguration(options);
                builder.Build();
            });

            Assert.Equal("SecretDisabled", ex.ErrorCode);
        }

        [Fact]
        public void WrongContentType()
        {
            string secretValue = "SecretValue from KeyVault";

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kvWrongContentType }));

            var builder = new ConfigurationBuilder();

            var options = new AzureAppConfigurationOptions()
            {
                Client = mockClient.Object
            };

            options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue));

            builder.AddAzureAppConfiguration(options);

            var config = builder.Build();

            Assert.NotEqual(secretValue, config[_kv.Key]);
        }

        [Fact]
        public void MultipleKeys()
        {
            string secretValue = "SecretValue from KeyVault";

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollectionPageOne));

            var builder = new ConfigurationBuilder();

            var options = new AzureAppConfigurationOptions()
            {
                Client = mockClient.Object
            };

            options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue) { });

            builder.AddAzureAppConfiguration(options);

            var config = builder.Build();

            Assert.Equal(secretValue, config["TK1"]);
            Assert.Equal(secretValue, config["TK2"]);
        }

        [Fact]
        public void CancellationToken()
        {
            string secretValue = "SecretValue from KeyVault";

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetSettingsAsync(new SettingSelector(KeyFilter.Any, LabelFilter.Null), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollectionPageOne));

            var builder = new ConfigurationBuilder();

            var options = new AzureAppConfigurationOptions()
            {
                Client = mockClient.Object
            };

            Assert.Throws<OperationCanceledException>(() =>
            {
                options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue)
                {
                    CancellationToken = new CancellationToken(true)
                });

                builder.AddAzureAppConfiguration(options);
                builder.Build();
            });
        }


        [Fact]
        public void HasNoAccessToKeyVault()
        {
            var KeyValues = new List<ConfigurationSetting> { _kv };
            string secretValue = "SecretValue from KeyVault";

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(KeyValues));

            var builder = new ConfigurationBuilder();

            var options = new AzureAppConfigurationOptions()
            {
                Client = mockClient.Object
            };

            KeyVaultReferenceException ex = Assert.Throws<KeyVaultReferenceException>(() =>
            {
                options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue) { HasAccessToKeyVault = false });
                builder.AddAzureAppConfiguration(options);
                builder.Build();
            });

            Assert.Equal("AccessDenied", ex.ErrorCode);
        }
    }
}
