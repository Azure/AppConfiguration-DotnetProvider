using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using System.Collections.Generic;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class KeyVaultReferenceTests
    {

        IKeyValue _kv = new KeyValue("TestKey1")
        {
            Value = @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }
                   ",
            ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c1",
            ContentType = KeyVaultConstants.ContentType + "; charset=utf-8"
        };

        IKeyValue _kvNoUrl = new KeyValue("TestKey1")
        {
            Value = "Test",
            ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c1",
            ContentType = KeyVaultConstants.ContentType + "; charset=utf-8"
        };



        IKeyValue _kvWrongContentType = new KeyValue("TestKey1")
        {
            Value = @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }",
            ETag = "c3c231fd -39a0-4cb6-3237-4614474b92c1",
            ContentType = "test"
        };


        IEnumerable<IKeyValue> _kvCollectionPageOne = new List<IKeyValue>
        {
             new KeyValue("TK1")
             {
                Value = @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }
                   ",
                ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c1",
                ContentType = KeyVaultConstants.ContentType + "; charset=utf-8"
             },
             new KeyValue("TK2")
             {
                Value = @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/Password3/6db5a48680104dda9097b1e6d859e553""
                    }
                   ",
                ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c1",
                ContentType = KeyVaultConstants.ContentType + "; charset=utf-8"
             },
        };

        [Fact]
        public void NotSecretIdentifierURI()
        {
            IEnumerable<IKeyValue> KeyValues = new List<IKeyValue> { _kvNoUrl };
            string secretValue = "SecretValue from KeyVault";

            using (var testClient = new AzconfigClient(TestHelpers.CreateMockEndpointString(),
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
            IEnumerable<IKeyValue> KeyValues = new List<IKeyValue> { _kv };
            string secretValue = "SecretValue from KeyVault";

            using (var testClient = new AzconfigClient(TestHelpers.CreateMockEndpointString(),
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
            IEnumerable<IKeyValue> KeyValues = new List<IKeyValue> { _kv };
            string secretValue = "SecretValue from KeyVault";

            using (var testClient = new AzconfigClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kv, KeyValues)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                Assert.Throws<KeyVaultErrorException>(() =>
                {
                    options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue) { IsEnabled = false });
                    builder.AddAzureAppConfiguration(options);
                    builder.Build();
                });
            }
        }


        [Fact]
        public void NonActiveSecretIdentifier()
        {

            IEnumerable<IKeyValue> KeyValues = new List<IKeyValue> { _kv };
            string secretValue = "SecretValue from KeyVault";

            using (var testClient = new AzconfigClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kv, KeyValues)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                Assert.Throws<KeyVaultErrorException>(() =>
                {
                    options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue) { IsActive = false });
                    builder.AddAzureAppConfiguration(options);
                    builder.Build();
                });
            }
        }

        [Fact]
        public void ExpiredSecretIdentifier()
        {

            IEnumerable<IKeyValue> KeyValues = new List<IKeyValue> { _kv };
            string secretValue = "SecretValue from KeyVault";

            using (var testClient = new AzconfigClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kv, KeyValues)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                Assert.Throws<KeyVaultErrorException>(() =>
                {
                    options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue) { IsNotExpired = false });
                    builder.AddAzureAppConfiguration(options);
                    builder.Build();
                });
            }
        }

        [Fact]
        public void WrongContentType()
        {

            IEnumerable<IKeyValue> KeyValues = new List<IKeyValue> { _kvWrongContentType };
            string secretValue = "SecretValue from KeyVault";


            using (var testClient = new AzconfigClient(TestHelpers.CreateMockEndpointString(),
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

            using (var testClient = new AzconfigClient(TestHelpers.CreateMockEndpointString(),
                                                       new MockedGetKeyValueRequest(_kv, _kvCollectionPageOne)))
            {
                var builder = new ConfigurationBuilder();

                var options = new AzureAppConfigurationOptions()
                {
                    Client = testClient
                };

                options.UseAzureKeyVault(new MockedAzureKeyVaultClient(secretValue));

                builder.AddAzureAppConfiguration(options);

                var config = builder.Build();

                Assert.Equal(secretValue, config["TK1"]);

                Assert.Equal(secretValue, config["TK2"]);
            }
        }
    }

}
