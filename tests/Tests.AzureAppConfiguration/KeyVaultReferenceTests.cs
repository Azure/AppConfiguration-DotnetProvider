// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Azure.Core.Testing;
using Azure.Data.AppConfiguration;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class KeyVaultReferenceTests
    {
        string _secretValue = "SecretValue from KeyVault";

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
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kvNoUrl }));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault"));

            IConfiguration configuration = null;
            var builder = new ConfigurationBuilder();

            var exception = Record.Exception(() =>
            {
                configuration = builder.AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                }).Build();
            });

            Assert.IsType<KeyVaultReferenceException>(exception);
            Assert.Null(configuration);
        }

        [Fact]
        public void UseSecret()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _secretValue))));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                })
                .Build();

            Assert.Equal(_secretValue, configuration[_kv.Key]);
        }

        [Fact]
        public void DisabledSecretIdentifier()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException(403, "Operation get is not allowed on a disabled secret.", "SecretDisabled", null));

            KeyVaultReferenceException ex = Assert.Throws<KeyVaultReferenceException>(() =>
            {
                new ConfigurationBuilder().AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                }).Build();
            });

            Assert.Equal("SecretDisabled", ex.ErrorCode);
        }

        [Fact]
        public void WrongContentType()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kvWrongContentType }));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                })
                .Build();

            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void MultipleKeys()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollectionPageOne));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _secretValue))));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                })
                .Build();

            Assert.Equal(_secretValue, config["TK1"]);
            Assert.Equal(_secretValue, config["TK2"]);
        }

        [Fact]
        public void CancellationToken()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(_kvCollectionPageOne));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Throws(new OperationCanceledException());

            Assert.Throws<OperationCanceledException>(() =>
            {
                new ConfigurationBuilder().AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                })
                .Build();
            });
        }


        [Fact]
        public void HasNoAccessToKeyVault()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException(403, "Access denied. Caller was not found on any access policy.", "AccessDenied", null));

            KeyVaultReferenceException ex = Assert.Throws<KeyVaultReferenceException>(() =>
            {
                new ConfigurationBuilder().AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                })
                .Build();
            });

            Assert.Equal("AccessDenied", ex.ErrorCode);
        }

        [Fact]
        public void RegisterMultipleClients()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var mockSecretClient1 = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient1.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));

            var mockSecretClient2 = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient2.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient2.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _secretValue))));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient1.Object)
                                                      .Register(mockSecretClient2.Object));
                })
                .Build();

            mockSecretClient1.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            mockSecretClient2.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.Equal(_secretValue, configuration[_kv.Key]);
        }

        [Fact]
        public void ServerRequestIsMadeWhenDefaultCredentialIsSet()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            KeyVaultReferenceException ex = Assert.Throws<KeyVaultReferenceException>(() =>
            {
                new ConfigurationBuilder().AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv => kv.SetCredential(new DefaultAzureCredential()));
                })
                .Build();
            });

            Assert.NotNull(ex.InnerException);
            Assert.IsType<AuthenticationFailedException>(ex.InnerException);
        }

        [Fact]
        public void ThrowsWhenNoMatchingSecretClientIsFound()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var mockSecretClient1 = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient1.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics1.vault.azure.net"));

            var mockSecretClient2 = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient2.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics2.vault.azure.net"));

            Assert.Throws<KeyVaultReferenceException>(() =>
            {
                new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient1.Object).Register(mockSecretClient2.Object));
                })
                .Build();
            });

            mockSecretClient1.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            mockSecretClient2.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void ThrowsWhenSecretClientAndSecretResolverAndDefaultCredentialAreMissing()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            Assert.Throws<KeyVaultReferenceException>(() =>
            {
                new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                })
                .Build();
            });
        }

        [Fact]
        public void DoesNotThrowKeyVaultExceptionWhenProviderIsOptional()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var mockKeyValueAdapter = new Mock<IKeyValueAdapter>(MockBehavior.Strict);
            mockKeyValueAdapter.Setup(adapter => adapter.CanProcess(_kv))
                .Returns(true);
            mockKeyValueAdapter.Setup(adapter => adapter.ProcessKeyValue(_kv, It.IsAny<CancellationToken>()))
                .Throws(new KeyVaultReferenceException("Key vault error", null));

            new ConfigurationBuilder()
            .AddAzureAppConfiguration(options =>
            {
                options.Client = mockClient.Object;
                options.Adapters = new List<IKeyValueAdapter> { mockKeyValueAdapter.Object };
            }, optional: true)
            .Build();
        }

        [Fact]
        public void CallsSecretResolverCallbackWhenNoMatchingSecretClientIsFound()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            IConfiguration config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetSecretResolver((secretUri) =>
                        {
                            return new ValueTask<string>(secretUri.ToString());
                        });
                    });
                })
                .Build();

            Assert.Equal("https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret", config["TestKey1"]);
        }

        [Fact]
        public void ThrowsWhenBothDefaultCredentialAndSecretResolverCallbackAreSet()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            {
                new ConfigurationBuilder().AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetSecretResolver((secretUri) =>
                        {
                            return new ValueTask<string>(secretUri.ToString());
                        });

                        kv.SetCredential(new DefaultAzureCredential());
                    });
                })
                .Build();
            });

            Assert.NotNull(ex.InnerException);
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        [Fact]
        public void LastKeyVaultOptionsWinWithMultipleConfigureKeyVaultCalls()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            IConfiguration config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetCredential(new DefaultAzureCredential());
                    });
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetSecretResolver((secretUri) =>
                        {
                            return new ValueTask<string>(secretUri.ToString());
                        });
                    });
                })
                .Build();

            Assert.Equal("https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret", config["TestKey1"]);
        }

        [Fact]
        public void DontUseSecretResolverCallbackWhenMatchingSecretClientIsPresent()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict, TestHelpers.CreateMockEndpointString());
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _secretValue))));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Client = mockClient.Object;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetSecretResolver((secretUri) =>
                        {
                            return new ValueTask<string>(secretUri.ToString());
                        });

                        kv.Register(mockSecretClient.Object);
                    });
                })
                .Build();

            Assert.Equal(_secretValue, configuration[_kv.Key]);
        }
    }
}
