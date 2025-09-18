// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Afd
{
    /// <summary>
    /// HTTP pipeline policy that removes Authorization headers from requests and orders query parameters by lowercase.
    /// </summary>
    internal class AfdPolicy : HttpPipelinePolicy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AfdPolicy"/> class.
        /// </summary>
        public AfdPolicy()
        {
        }

        /// <summary>
        /// Processes the HTTP message, removes the Authorization header, and orders query parameters by lowercase.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            message.Request.Headers.Remove("Authorization");

            message.Request.Uri.Reset(OrderQueryParameters(message.Request.Uri.ToUri()));

            ProcessNext(message, pipeline);
        }

        /// <summary>
        /// Processes the HTTP message asynchronously, removes the Authorization header, and orders query parameters by lowercase.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async System.Threading.Tasks.ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            message.Request.Headers.Remove("Authorization");

            message.Request.Uri.Reset(OrderQueryParameters(message.Request.Uri.ToUri()));

            await ProcessNextAsync(message, pipeline).ConfigureAwait(false);
        }

        private static Uri OrderQueryParameters(Uri uri)
        {
            var uriBuilder = new UriBuilder(uri);

            NameValueCollection query = HttpUtility.ParseQueryString(uriBuilder.Query);

            if (query.Count > 0)
            {
                IEnumerable<string> orderedParams = query.AllKeys
                    .Where(key => key != null)
                    .OrderBy(key => key.ToLowerInvariant())
                    .Select(key =>
                    {
                        string value = query[key];

                        if (value == null)
                        {
                            return Uri.EscapeDataString(key);
                        }

                        return $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
                    });

                uriBuilder.Query = string.Join("&", orderedParams);
            }

            return uriBuilder.Uri;
        }
    }
}
