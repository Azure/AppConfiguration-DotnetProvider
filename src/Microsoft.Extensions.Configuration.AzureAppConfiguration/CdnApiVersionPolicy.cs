// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Web;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// A policy that adds the API version query parameter to HTTP requests.
    /// </summary>
    public class CdnApiVersionPolicy : HttpPipelinePolicy
    {
        /// <summary>
        /// Processes the HTTP message by adding the API version query parameter.
        /// </summary>
        /// <param name="message">The HTTP message to process.</param>
        /// <param name="pipeline">The pipeline of HTTP policies to apply.</param>
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            message.Request.Uri.Reset(AlterApiVersion(message.Request.Uri.ToUri()));

            ProcessNext(message, pipeline);
        }

        /// <summary>
        /// Processes the HTTP message asynchronously by adding the API version query parameter.
        /// </summary>
        /// <param name="message">The HTTP message to process.</param>
        /// <param name="pipeline">The pipeline of HTTP policies to apply.</param>
        /// <returns>A ValueTask representing the asynchronous operation.</returns>
        public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            message.Request.Uri.Reset(AlterApiVersion(message.Request.Uri.ToUri()));

            await ProcessNextAsync(message, pipeline).ConfigureAwait(false);
        }

        private static Uri AlterApiVersion(Uri uri)
        {
            var uriBuilder = new UriBuilder(uri);

            NameValueCollection query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query["api-version"] = "2024-09-01-preview";

            uriBuilder.Query = query.ToString();

            return uriBuilder.Uri;
        }
    }
}
