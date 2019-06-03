using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class AzconfigClientExtensions
    {
        public static async Task<IKeyValue> GetCurrentKeyValue(this AzconfigClient client, IKeyValue keyValue, CancellationToken cancellationToken)
        {
            SetETag(keyValue.ETag);

            var options = new QueryKeyValueOptions() { Label = keyValue.Label };
            IKeyValue kv = await client.GetKeyValue(keyValue.Key, options, cancellationToken);

            ClearETag();

            return kv;
        }

        private static void SetETag(string ETag)
        {
            RequestWithETagOptimizationDelegatingHandler.UseETag = true;
            RequestWithETagOptimizationDelegatingHandler.ETag = ETag;
        }

        private static void ClearETag()
        {
            RequestWithETagOptimizationDelegatingHandler.UseETag = false;
            RequestWithETagOptimizationDelegatingHandler.ETag = null;
        }
    }
}
