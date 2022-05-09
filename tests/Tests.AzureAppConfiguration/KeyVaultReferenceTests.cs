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
        string _certValue = "Certificate Value from KeyVault";
        string _secretUri = "https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret";

        ConfigurationSetting sentinelKv = new ConfigurationSetting("Sentinel", "Value1");

        ConfigurationSetting _kv = ConfigurationModelFactory.ConfigurationSetting(
            key: "TestKey1",
            value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }",
            eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
            contentType: KeyVaultConstants.ContentType + "; charset=utf-8");

        ConfigurationSetting _kvCertRef = ConfigurationModelFactory.ConfigurationSetting(
            key: "TestCertificateKey",
            value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/certificates/TestCertificate""
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
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
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
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                })
                .Build();

            Assert.Equal(_secretValue, configuration[_kv.Key]);
        }

        [Fact]
        public void UseCertificate()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kvCertRef }));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _certValue))));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                })
                .Build();

            Assert.Equal(_certValue, configuration[_kvCertRef.Key]);
        }

        [Fact]
        public void ThrowsWhenSecretNotFound()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException(404, "Secret Not Found.", "SecretNotFound", null));

            KeyVaultReferenceException ex = Assert.Throws<KeyVaultReferenceException>(() =>
            {
                new ConfigurationBuilder().AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                }).Build();
            });

            Assert.Equal("SecretNotFound", ex.ErrorCode);
        }

        [Fact]
        public void DisabledSecretIdentifier()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                }).Build();
            });

            Assert.Equal("SecretDisabled", ex.ErrorCode);
        }

        [Fact]
        public void WrongContentType()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kvWrongContentType }));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));

            var configuration = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                })
                .Build();

            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void MultipleKeys()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);
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
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient.Object));
                })
                .Build();
            });
        }


        [Fact]
        public void HasNoAccessToKeyVault()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
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
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
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
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            KeyVaultReferenceException ex = Assert.Throws<KeyVaultReferenceException>(() =>
            {
                new ConfigurationBuilder().AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv => kv.SetCredential(new DefaultAzureCredential()));
                })
                .Build();
            });

            Assert.NotNull(ex.InnerException);
        }

        [Fact]
        public void ThrowsWhenNoMatchingSecretClientIsFound()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv => kv.Register(mockSecretClient1.Object).Register(mockSecretClient2.Object));
                })
                .Build();
            });

            mockSecretClient1.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            mockSecretClient2.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void ThrowsWhenConfigureKeyVaultIsMissing()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            Assert.Throws<KeyVaultReferenceException>(() =>
            {
                new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                })
                .Build();
            });
        }

        [Fact]
        public void DoesNotThrowKeyVaultExceptionWhenProviderIsOptional()
        {
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var mockKeyValueAdapter = new Mock<IKeyValueAdapter>(MockBehavior.Strict);
            mockKeyValueAdapter.Setup(adapter => adapter.CanProcess(_kv))
                .Returns(true);
            mockKeyValueAdapter.Setup(adapter => adapter.ProcessKeyValue(_kv, It.IsAny<CancellationToken>()))
                .Throws(new KeyVaultReferenceException("Key vault error", null));
            mockKeyValueAdapter.Setup(adapter => adapter.InvalidateCache(null));

            new ConfigurationBuilder()
            .AddAzureAppConfiguration(options =>
            {
                options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                options.Adapters = new List<IKeyValueAdapter> { mockKeyValueAdapter.Object };
            }, optional: true)
            .Build();
        }

        [Fact]
        public void CallsSecretResolverCallbackWhenNoMatchingSecretClientIsFound()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            IConfiguration config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetSecretResolver((secretUri) =>
                        {
                            return new ValueTask<string>(secretUri.ToString());
                        });
                    });
                })
                .Build();

            Assert.Equal(_secretUri, config["TestKey1"]);
        }

        [Fact]
        public void ThrowsWhenBothDefaultCredentialAndSecretResolverCallbackAreSet()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            {
                new ConfigurationBuilder().AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
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
        public void ThrowsWhenSecretResolverIsNull()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            Assert.Throws<ArgumentNullException>(() =>
            {
                new ConfigurationBuilder().AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetSecretResolver(null);
                    });
                })
                .Build();
            });
        }

        [Fact]
        public void LastKeyVaultOptionsWinWithMultipleConfigureKeyVaultCalls()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            IConfiguration config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
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

            Assert.Equal(_secretUri, config["TestKey1"]);
        }

        [Fact]
        public void DontUseSecretResolverCallbackWhenMatchingSecretClientIsPresent()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
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

        [Fact]
        public void ThrowsWhenSecretRefreshIntervalIsTooShort()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                new ConfigurationBuilder().AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetSecretRefreshInterval(_kv.Key, TimeSpan.FromMilliseconds(10));
                    });
                })
                .Build();
            });
        }

        [Fact]
        public void SecretIsReturnedFromCacheIfSecretCacheHasNotExpired()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan cacheExpirationTime = TimeSpan.FromSeconds(1);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                return Response.FromValue(TestHelpers.CloneSetting(sentinelKv), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var unchanged = sentinelKv.Key == setting.Key && sentinelKv.Label == setting.Label && sentinelKv.Value == setting.Value;
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(sentinelKv, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _secretValue))));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;

                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                        kv.SetSecretRefreshInterval(_kv.Key, TimeSpan.FromDays(1));
                    });

                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("Sentinel")
                                      .SetCacheExpiration(cacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Value1", config["Sentinel"]);
            Assert.Equal(_secretValue, config[_kv.Key]);

            // Update sentinel key-value
            sentinelKv.Value = "Value2";
            Thread.Sleep(cacheExpirationTime);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Value2", config["Sentinel"]);
            Assert.Equal(_secretValue, config[_kv.Key]);

            // Validate that only 1 call was made to fetch secrets from KeyVault
            // Since Key Vault refresh interval has not elapsed, the sentinel key change should fetch secret from Key Vault
            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void CachedSecretIsInvalidatedWhenRefreshAllIsTrue()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan cacheExpirationTime = TimeSpan.FromSeconds(1);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);

            Response<ConfigurationSetting> GetTestKey(string key, string label, CancellationToken cancellationToken)
            {
                return Response.FromValue(TestHelpers.CloneSetting(sentinelKv), mockResponse.Object);
            }

            Response<ConfigurationSetting> GetIfChanged(ConfigurationSetting setting, bool onlyIfChanged, CancellationToken cancellationToken)
            {
                var unchanged = sentinelKv.Key == setting.Key && sentinelKv.Label == setting.Label && sentinelKv.Value == setting.Value;
                var response = new MockResponse(unchanged ? 304 : 200);
                return Response.FromValue(sentinelKv, response);
            }

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<string, string, CancellationToken, Response<ConfigurationSetting>>)GetTestKey);

            mockClient.Setup(c => c.GetConfigurationSettingAsync(It.IsAny<ConfigurationSetting>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Func<ConfigurationSetting, bool, CancellationToken, Response<ConfigurationSetting>>)GetIfChanged);

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _secretValue))));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                        kv.SetSecretRefreshInterval(_kv.Key, TimeSpan.FromDays(1));
                    });

                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("Sentinel", refreshAll: true)
                                      .SetCacheExpiration(cacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Value1", config["Sentinel"]);
            Assert.Equal(_secretValue, config[_kv.Key]);

            // Update sentinel key-value to trigger refresh operation
            sentinelKv.Value = "Value2";
            Thread.Sleep(cacheExpirationTime);
            refresher.RefreshAsync().Wait();

            Assert.Equal("Value2", config["Sentinel"]);
            Assert.Equal(_secretValue, config[_kv.Key]);

            // Validate that 2 calls were made to fetch secrets from KeyVault
            // Even though Key Vault refresh interval has not elapsed, refreshAll trigger should fetch secret from Key Vault again
            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public void SecretIsReloadedFromKeyVaultWhenCacheExpires()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan cacheExpirationTime = TimeSpan.FromSeconds(1);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns(new MockAsyncPageable(new List<ConfigurationSetting> { _kv }));

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _secretValue))));

            var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                        kv.SetSecretRefreshInterval(_kv.Key, cacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal(_secretValue, config[_kv.Key]);

            // Sleep to let the secret cache expire 
            Thread.Sleep(cacheExpirationTime);
            refresher.RefreshAsync().Wait();

            Assert.Equal(_secretValue, config[_kv.Key]);

            // Validate that 2 calls were made to fetch secrets from KeyVault because the secret cache had expired. 
            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public void SecretsWithDefaultRefreshInterval()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan shortCacheExpirationTime = TimeSpan.FromSeconds(1);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                        kv.SetSecretRefreshInterval(shortCacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal(_secretValue, config["TK1"]);
            Assert.Equal(_secretValue, config["TK2"]);

            // Sleep to let the secret cache expire for both secrets 
            Thread.Sleep(shortCacheExpirationTime);
            refresher.RefreshAsync().Wait();

            Assert.Equal(_secretValue, config["TK1"]);
            Assert.Equal(_secretValue, config["TK2"]);

            // Validate that 4 calls were made to fetch secrets from KeyVault because the secret cache had expired for both secrets. 
            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        }

        [Fact]
        public void SecretsWithDifferentRefreshIntervals()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan shortCacheExpirationTime = TimeSpan.FromSeconds(1);
            TimeSpan longCacheExpirationTime = TimeSpan.FromDays(1);

            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
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
                    options.ClientProvider = TestHelpers.CreateMockedConfigurationClientProvider(mockClient.Object);;
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                        kv.SetSecretRefreshInterval("TK1", shortCacheExpirationTime);
                        kv.SetSecretRefreshInterval(longCacheExpirationTime);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal(_secretValue, config["TK1"]);
            Assert.Equal(_secretValue, config["TK2"]);

            // Sleep to let the secret cache expire for one secret 
            Thread.Sleep(shortCacheExpirationTime);
            refresher.RefreshAsync().Wait();

            Assert.Equal(_secretValue, config["TK1"]);
            Assert.Equal(_secretValue, config["TK2"]);

            // Validate that 3 calls were made to fetch secrets from KeyVault because the secret cache had expired for only one secret. 
            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        }
    }
}
