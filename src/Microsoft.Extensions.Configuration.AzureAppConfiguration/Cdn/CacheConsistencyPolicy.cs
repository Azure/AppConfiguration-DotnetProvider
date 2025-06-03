// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using System;
using System.Diagnostics;
using System.Web;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Cdn
{
    /// <summary>
    /// HTTP pipeline policy that injects consistency token into the query string for CDN cache consistency.
    /// </summary>
    internal class CacheConsistencyPolicy : HttpPipelinePolicy
    {
        private readonly ICacheConsistencyTokenAccessor _cacheConsistencyAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheConsistencyPolicy"/> class.
        /// </summary>
        /// <param name="cacheConsistencyAccessor">The CDN cache consistency accessor.</param>        
        public CacheConsistencyPolicy(ICacheConsistencyTokenAccessor cacheConsistencyAccessor)
        {
            _cacheConsistencyAccessor = cacheConsistencyAccessor ?? throw new ArgumentNullException(nameof(cacheConsistencyAccessor));
        }

        /// <summary>
        /// Processes the HTTP message and injects token into query string if CDN cache consistency is enabled.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            string token = _cacheConsistencyAccessor.Current;
            if (!string.IsNullOrEmpty(token))
            {
                message.Request.Uri.Reset(AddTokenToUri(message.Request.Uri.ToUri(), token));
            }

            ProcessNext(message, pipeline);
        }

        /// <summary>
        /// Processes the HTTP message asynchronously and injects token into query string if CDN cache consistency is enabled.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async System.Threading.Tasks.ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            string token = _cacheConsistencyAccessor.Current;
            if (!string.IsNullOrEmpty(token))
            {
                message.Request.Uri.Reset(AddTokenToUri(message.Request.Uri.ToUri(), token));
            }

            await ProcessNextAsync(message, pipeline).ConfigureAwait(false);
        }

        private static Uri AddTokenToUri(Uri uri, string token)
        {
            Debug.Assert(!string.IsNullOrEmpty(token));

            var uriBuilder = new UriBuilder(uri);

            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query["_"] = token;

            uriBuilder.Query = query.ToString();

            return uriBuilder.Uri;
        }
    }
}
