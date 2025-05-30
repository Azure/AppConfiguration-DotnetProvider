﻿// Copyright (c) Microsoft Corporation.
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
using System.Linq;
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

        List<ConfigurationSetting> _invalidJsonKvCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key:"MissingClosingBracket",
                value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                   ",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8"),

            ConfigurationModelFactory.ConfigurationSetting(
                key:"MissingOpeningBracket",
                value: @"
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }
                   ",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8"),

            ConfigurationModelFactory.ConfigurationSetting(
                key:"MissingUriInRootJson",
                value: @"
                    {
                        {
                            ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                        }
                    }
                   ",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8"),

            ConfigurationModelFactory.ConfigurationSetting(
                key:"UriValueInsideObject",
                value: @"
                    {
                        {
                            ""uri"": {
                                ""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                            }
                        }
                    }
                   ",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8")
        };

        List<ConfigurationSetting> _validJsonKvCollection = new List<ConfigurationSetting>
        {
            ConfigurationModelFactory.ConfigurationSetting(
                key:"AdditionalProperty1",
                value: @"
                    {
                        ""additional_property"":""additional_property"",
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }
                   ",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8"),

            ConfigurationModelFactory.ConfigurationSetting(
                key:"AdditionalProperty2",
                value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret"",
                        ""additional_property"": {
                            ""inside_property"": ""inside_property""
                        }
                    }
                   ",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8"),

            ConfigurationModelFactory.ConfigurationSetting(
                key:"DuplicateUri",
                value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/certificates/TestCertificate"",
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }
                   ",
                eTag: new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                contentType: KeyVaultConstants.ContentType + "; charset=utf-8")
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ConfigureStartupOptions(startupOptions =>
                    {
                        startupOptions.Timeout = TimeSpan.FromSeconds(5);
                    });
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
            mockKeyValueAdapter.Setup(adapter => adapter.CanProcess(It.IsAny<ConfigurationSetting>()))
                .Returns(true);
            mockKeyValueAdapter.Setup(adapter => adapter.ProcessKeyValue(It.IsAny<ConfigurationSetting>(), It.IsAny<Uri>(), It.IsAny<Logger>(), It.IsAny<CancellationToken>()))
                .Throws(new KeyVaultReferenceException("Key vault error", null));
            mockKeyValueAdapter.Setup(adapter => adapter.OnChangeDetected(null));
            mockKeyValueAdapter.Setup(adapter => adapter.OnConfigUpdated());

            new ConfigurationBuilder()
            .AddAzureAppConfiguration(options =>
            {
                options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.SetSecretRefreshInterval(_kv.Key, TimeSpan.FromMilliseconds(10));
                    });
                })
                .Build();
            });
        }

        [Fact]
        public async Task SecretIsReturnedFromCacheIfSecretCacheHasNotExpired()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan refreshInterval = TimeSpan.FromSeconds(1);

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
                return Response.FromValue(TestHelpers.CloneSetting(sentinelKv), response);
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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);

                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                        kv.SetSecretRefreshInterval(_kv.Key, TimeSpan.FromDays(1));
                    });

                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("Sentinel")
                                      .SetRefreshInterval(refreshInterval);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Value1", config["Sentinel"]);
            Assert.Equal(_secretValue, config[_kv.Key]);

            // Update sentinel key-value
            sentinelKv = TestHelpers.ChangeValue(sentinelKv, "Value2");
            Thread.Sleep(refreshInterval);
            await refresher.RefreshAsync();

            Assert.Equal("Value2", config["Sentinel"]);
            Assert.Equal(_secretValue, config[_kv.Key]);

            // Validate that only 1 call was made to fetch secrets from KeyVault
            // Since Key Vault refresh interval has not elapsed, the sentinel key change should fetch secret from Key Vault
            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CachedSecretIsInvalidatedWhenRefreshAllIsTrue()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan refreshInterval = TimeSpan.FromSeconds(60);

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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                        kv.SetSecretRefreshInterval(_kv.Key, TimeSpan.FromDays(1));
                    });

                    options.ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register("Sentinel", refreshAll: true)
                                      .SetRefreshInterval(refreshInterval);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal("Value1", config["Sentinel"]);
            Assert.Equal(_secretValue, config[_kv.Key]);

            // Update sentinel key-value to trigger refresh operation
            sentinelKv = TestHelpers.ChangeValue(sentinelKv, "Value2");
            Thread.Sleep(refreshInterval);
            await refresher.RefreshAsync();

            Assert.Equal("Value2", config["Sentinel"]);
            Assert.Equal(_secretValue, config[_kv.Key]);

            // Validate that 2 calls were made to fetch secrets from KeyVault
            // Even though Key Vault refresh interval has not elapsed, refreshAll trigger should fetch secret from Key Vault again
            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task SecretIsReloadedFromKeyVaultWhenCacheExpires()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan refreshInterval = TimeSpan.FromSeconds(60);

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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                        kv.SetSecretRefreshInterval(_kv.Key, refreshInterval);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal(_secretValue, config[_kv.Key]);

            // Sleep to let the secret cache expire 
            Thread.Sleep(refreshInterval);
            await refresher.RefreshAsync();

            Assert.Equal(_secretValue, config[_kv.Key]);

            // Validate that 2 calls were made to fetch secrets from KeyVault because the secret cache had expired. 
            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task SecretsWithDefaultRefreshInterval()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan shortRefreshInterval = TimeSpan.FromSeconds(60);

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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                        kv.SetSecretRefreshInterval(shortRefreshInterval);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal(_secretValue, config["TK1"]);
            Assert.Equal(_secretValue, config["TK2"]);

            // Sleep to let the secret cache expire for both secrets 
            Thread.Sleep(shortRefreshInterval);
            await refresher.RefreshAsync();

            Assert.Equal(_secretValue, config["TK1"]);
            Assert.Equal(_secretValue, config["TK2"]);

            // Validate that 4 calls were made to fetch secrets from KeyVault because the secret cache had expired for both secrets. 
            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        }

        [Fact]
        public async Task SecretsWithDifferentRefreshIntervals()
        {
            IConfigurationRefresher refresher = null;
            TimeSpan shortRefreshInterval = TimeSpan.FromSeconds(60);
            TimeSpan longRefreshInterval = TimeSpan.FromDays(1);

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
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(mockClient.Object);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                        kv.SetSecretRefreshInterval("TK1", shortRefreshInterval);
                        kv.SetSecretRefreshInterval(longRefreshInterval);
                    });

                    refresher = options.GetRefresher();
                })
                .Build();

            Assert.Equal(_secretValue, config["TK1"]);
            Assert.Equal(_secretValue, config["TK2"]);

            // Sleep to let the secret cache expire for one secret 
            Thread.Sleep(shortRefreshInterval);
            await refresher.RefreshAsync();

            Assert.Equal(_secretValue, config["TK1"]);
            Assert.Equal(_secretValue, config["TK2"]);

            // Validate that 3 calls were made to fetch secrets from KeyVault because the secret cache had expired for only one secret. 
            mockSecretClient.Verify(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        [Fact]
        public void ThrowsWhenInvalidKeyVaultSecretReferenceJson()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var cacheExpiration = TimeSpan.FromSeconds(1);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns((Func<SettingSelector, CancellationToken, MockAsyncPageable>)GetTestKeys);

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));

            MockAsyncPageable GetTestKeys(SettingSelector selector, CancellationToken ct)
            {
                var copy = new List<ConfigurationSetting>();
                var newSetting = _invalidJsonKvCollection.FirstOrDefault(s => s.Key == selector.KeyFilter);
                if (newSetting != null)
                    copy.Add(TestHelpers.CloneSetting(newSetting));
                return new MockAsyncPageable(copy);
            }

            var testClient = mockClient.Object;

            foreach (ConfigurationSetting setting in _invalidJsonKvCollection)
            {
                void action() => new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Select(setting.Key);
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                    });
                })
                .Build();

                // Each of the secret references should throw an exception when parsed
                Assert.Throws<KeyVaultReferenceException>(action);
            }
        }

        [Fact]
        public void AlternateValidKeyVaultSecretReferenceJsons()
        {
            var mockResponse = new Mock<Response>();
            var mockClient = new Mock<ConfigurationClient>(MockBehavior.Strict);
            var cacheExpiration = TimeSpan.FromSeconds(1);

            mockClient.Setup(c => c.GetConfigurationSettingsAsync(It.IsAny<SettingSelector>(), It.IsAny<CancellationToken>()))
                .Returns((Func<SettingSelector, CancellationToken, MockAsyncPageable>)GetTestKeys);

            var mockSecretClient = new Mock<SecretClient>(MockBehavior.Strict);
            mockSecretClient.SetupGet(client => client.VaultUri).Returns(new Uri("https://keyvault-theclassics.vault.azure.net"));
            mockSecretClient.Setup(client => client.GetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string name, string version, CancellationToken cancellationToken) =>
                    Task.FromResult((Response<KeyVaultSecret>)new MockResponse<KeyVaultSecret>(new KeyVaultSecret(name, _secretValue))));

            MockAsyncPageable GetTestKeys(SettingSelector selector, CancellationToken ct)
            {
                var copy = new List<ConfigurationSetting>();
                var newSetting = _validJsonKvCollection.FirstOrDefault(s => s.Key == selector.KeyFilter);
                if (newSetting != null)
                    copy.Add(TestHelpers.CloneSetting(newSetting));
                return new MockAsyncPageable(copy);
            }

            var testClient = mockClient.Object;

            foreach (ConfigurationSetting setting in _validJsonKvCollection)
            {
                var config = new ConfigurationBuilder()
                .AddAzureAppConfiguration(options =>
                {
                    options.Select(setting.Key);
                    options.ClientManager = TestHelpers.CreateMockedConfigurationClientManager(testClient);
                    options.ConfigureKeyVault(kv =>
                    {
                        kv.Register(mockSecretClient.Object);
                    });
                })
                .Build();

                // Each of the secret references should work as normal and use the uri
                Assert.Equal(_secretValue, config[setting.Key]);
            }
        }
    }
}
