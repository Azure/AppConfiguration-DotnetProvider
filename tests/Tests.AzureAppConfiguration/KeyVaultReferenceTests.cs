using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVault;
using Moq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Tests.AzureAppConfiguration
{
    public class KeyVaultReferenceTests
    {
        IKeyValue _kv = new KeyValue("mySecret")
        {
            Value = @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/secrets/TheTrialSecret""
                    }
                   ",
            ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c1",
            ContentType = KeyVaultConstants.ContentType + "; charset=utf-8"
        };

        [Fact]
        public void KeyVaultUse()
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
    }
}
