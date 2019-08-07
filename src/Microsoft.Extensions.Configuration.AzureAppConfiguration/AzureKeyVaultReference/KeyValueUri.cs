using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.AzureKeyVaultReference
{
    class KeyValueUri
    {
        public string value { get; set; }
        private string uri { get; set; }


        KeyValueUri(string uri, string value) {
            this.uri = uri;
            this.value = value;
        }

    }
}
