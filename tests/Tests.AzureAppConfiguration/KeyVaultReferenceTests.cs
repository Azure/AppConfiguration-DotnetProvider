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
        private KeyValue _kv = new KeyValue("mySecret")
        {
            Value = @"
                    {
                        ""uri"":""https://keyvault-theclassics.vault.azure.net/TheTrialSecret""
                    }
                   ",
            ETag = "c3c231fd-39a0-4cb6-3237-4614474b92c1",
            ContentType = KeyVaultConstants.ContentType + "; charset=utf-8"
        };

        //Check successfull result for GetSecretFromKeyVault method
        [Fact]
        public async Task GetSecretFromKeyVault_Success()
        {
            var azureKeyVaultKeyValueAdapter = new AzureKeyVaultKeyValueAdapter();

            var builder = new ConfigurationBuilder();
            var config = builder.Build();
            //KeyVaultSecretReference secretRef = JsonConvert.DeserializeObject<KeyVaultSecretReference>(keyValue.Value, s_SerializationSettings);

            string secretUri = ".appconfig.keyvault/TheTrialSecret";
            // string expected = await azureKeyVaultKeyValueAdapter.GetSecretFromKeyVault(secretRef.Uri);
            var expected = await azureKeyVaultKeyValueAdapter.GetSecretFromKeyVault(secretUri, async () => await Task.Run(() => new SecretBundle() { Value = "newVersion" } ));
            var actual = "newVersion";
            Assert.Equal(actual, expected);
        }

        
    }

    public static class MockedKeyVaultClient
    {
        public static Task<SecretBundle> GetSecretAsync(this KeyVaultClient client, string secretIdentifier)
        {
            return Task.FromResult(new SecretBundle() { Value = "newVersion" });
        }
    }
}
