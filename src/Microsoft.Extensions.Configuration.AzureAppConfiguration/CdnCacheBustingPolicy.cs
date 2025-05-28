// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Core;
using Azure.Core.Pipeline;
using System;
using System.Diagnostics;
using System.Web;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// HTTP pipeline policy that injects ETags into the query string for CDN cache busting.
    /// </summary>
    internal class CdnCacheBustingPolicy : HttpPipelinePolicy
    {
        private readonly ICdnCacheBustingAccessor _cacheBustingAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="CdnCacheBustingPolicy"/> class.
        /// </summary>
        /// <param name="cacheBustingAccessor">The CDN cache busting accessor.</param>
        public CdnCacheBustingPolicy(ICdnCacheBustingAccessor cacheBustingAccessor)
        {
            _cacheBustingAccessor = cacheBustingAccessor ?? throw new ArgumentNullException(nameof(cacheBustingAccessor));
        }

        /// <summary>
        /// Processes the HTTP message and injects ETag into query string if CDN cache busting is enabled.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            string etag = _cacheBustingAccessor.CurrentETag;
            if (!string.IsNullOrEmpty(etag))
            {
                // Add ETag to the request URI
                message.Request.Uri.Reset(AddCacheBustingToUri(message.Request.Uri.ToUri(), etag));
            }

            ProcessNext(message, pipeline);
        }

        /// <summary>
        /// Processes the HTTP message asynchronously and injects ETag into query string if CDN cache busting is enabled.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async System.Threading.Tasks.ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            string etag = _cacheBustingAccessor.CurrentETag;
            if (!string.IsNullOrEmpty(etag))
            {
                // Add ETag to the request URI
                message.Request.Uri.Reset(AddCacheBustingToUri(message.Request.Uri.ToUri(), etag));
            }

            await ProcessNextAsync(message, pipeline).ConfigureAwait(false);
        }

        private static Uri AddCacheBustingToUri(Uri uri, string etag)
        {
            Debug.Assert(!string.IsNullOrEmpty(etag));

            var uriBuilder = new UriBuilder(uri);

            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query["cdn-cache-bust"] = etag;

            uriBuilder.Query = query.ToString();

            return uriBuilder.Uri;
        }
    }
}
