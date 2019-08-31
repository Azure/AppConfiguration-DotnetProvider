using Azure.Core.Http;
using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class KeyVaultReferenceTests
    {

        ConfigurationSetting _kv = new ConfigurationSetting("TestKey1",
            value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    })")
        {
            ETag = new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
            ContentType = KeyVaultConstants.ContentType + "; charset=utf-8"
        };

        ConfigurationSetting _kvNoUrl = new ConfigurationSetting("TestKey1", "Test")
        {
            ETag = new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
            ContentType = KeyVaultConstants.ContentType + "; charset=utf-8"
        };

        ConfigurationSetting _kvWrongContentType = new ConfigurationSetting("TestKey1",
        
            value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }")
        { 
            ETag = new ETag("c3c231fd -39a0-4cb6-3237-4614474b92c1"),
            ContentType = "test"
        };


        IEnumerable<ConfigurationSetting> _kvCollectionPageOne = new List<ConfigurationSetting>
        {
             new ConfigurationSetting("TK1",
             
                value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }
                   ")
             {
                ETag = new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                ContentType = KeyVaultConstants.ContentType + "; charset=utf-8"
             },
             new ConfigurationSetting("TK2",
             
                value: @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/Password3/6db5a48680104dda9097b1e6d859e553""
                    }
                   ")
             {
                ETag = new ETag("c3c231fd-39a0-4cb6-3237-4614474b92c1"),
                ContentType = KeyVaultConstants.ContentType + "; charset=utf-8"
             },
        };

        [Fact]
        public void NotSecretIdentifierURI()
        {
            IEnumerable<ConfigurationSetting> KeyValues = new List<ConfigurationSetting> { _kvNoUrl };
            string secretValue = "SecretValue from KeyVault";

            using (var testClient = new ConfigurationClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kvNoUrl, KeyValues)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
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
        }

        [Fact]
        public void UseSecret()
        {
            IEnumerable<ConfigurationSetting> KeyValues = new List<ConfigurationSetting> { _kv };
            string secretValue = "SecretValue from KeyVault";

            using (var testClient = new ConfigurationClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kv, KeyValues)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue));

                builder.AddAzureAppConfiguration(options);

                var config = builder.Build();

                Assert.Equal(secretValue, config[_kv.Key]);
            }
        }

        [Fact]
        public void DisabledSecretIdentifier()
        {
            IEnumerable<ConfigurationSetting> KeyValues = new List<ConfigurationSetting> { _kv };
            string secretValue = "SecretValue from KeyVault";

            using (var testClient = new ConfigurationClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kv, KeyValues)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                KeyVaultReferenceException ex = Assert.Throws<KeyVaultReferenceException>(() =>
                {
                    options.UseAzureKeyVault(new MockedAzureKeyVaultClient(_kv, secretValue) { IsEnabled = false });
                    builder.AddAzureAppConfiguration(options);
                    builder.Build();
                });

                Assert.Equal("SecretDisabled", ex.ErrorCode);
            }
        }

        [Fact]
        public void WrongContentType()
        {

            IEnumerable<ConfigurationSetting> KeyValues = new List<ConfigurationSetting> { _kvWrongContentType };
            string secretValue = "SecretValue from KeyVault";


            using (var testClient = new ConfigurationClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kvWrongContentType, KeyValues)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue));

                builder.AddAzureAppConfiguration(options);

                var config = builder.Build();

                Assert.NotEqual(secretValue, config[_kv.Key]);
            }
        }

        [Fact]
        public void MultipleKeys()
        {
            string secretValue = "SecretValue from KeyVault";

            using (var testClient = new ConfigurationClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kv, _kvCollectionPageOne)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue) { });

                builder.AddAzureAppConfiguration(options);

                var config = builder.Build();

                Assert.Equal(secretValue, config["TK1"]);

                Assert.Equal(secretValue, config["TK2"]);
            }
        }

        [Fact]
        public void CancelationToken()
        {
            string secretValue = "SecretValue from KeyVault";


            using (var testClient = new ConfigurationClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kv, _kvCollectionPageOne)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
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
        }


        [Fact]
        public void HasNoAccessToKeyVault()
        {
            IEnumerable<ConfigurationSetting> KeyValues = new List<ConfigurationSetting> { _kv };
            string secretValue = "SecretValue from KeyVault";

            using (var testClient = new ConfigurationClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kv, KeyValues)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                KeyVaultReferenceException ex = Assert.Throws<KeyVaultReferenceException>(() =>
                {
                    options.UseAzureKeyVault(new MockedAzureKeyVaultClient(_kv, secretValue) { HasAccessToKeyVault = false });
                    builder.AddAzureAppConfiguration(options);
                    builder.Build();
                });

                Assert.Equal("AccessDenied", ex.ErrorCode);
            }
        }
    }

}
