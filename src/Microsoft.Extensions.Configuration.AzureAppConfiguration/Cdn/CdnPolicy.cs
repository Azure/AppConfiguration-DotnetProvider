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
    /// HTTP pipeline policy that injects current token into the query string for CDN cache breakage and consistency.
    /// The injected token ensures CDN cache invalidation when configuration changes are detected and maintaining eventual consistency across distributed instances.
    /// </summary>
    internal class CdnPolicy : HttpPipelinePolicy
    {
        private readonly ICdnTokenAccessor _cdnTokenAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="CdnPolicy"/> class.
        /// </summary>
        /// <param name="cdnTokenAccessor">The token accessor that provides current token to be used for CDN cache breakage/consistency.</param>
        public CdnPolicy(ICdnTokenAccessor cdnTokenAccessor)
        {
            _cdnTokenAccessor = cdnTokenAccessor ?? throw new ArgumentNullException(nameof(cdnTokenAccessor));
        }

        /// <summary>
        /// Processes the HTTP message and injects token into query string to break CDN cache when changes are detected.
        /// This ensures fresh configuration data is retrieved when sentinel keys or collections have been modified.
        /// It also maintains eventual consistency across distributed instances by ensuring that the same token is used for all subsequent watch requests, until a new change is detected.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            string token = _cdnTokenAccessor.Current;
            if (!string.IsNullOrEmpty(token))
            {
                message.Request.Uri.Reset(AddTokenToUri(message.Request.Uri.ToUri(), token));
            }

            ProcessNext(message, pipeline);
        }

        /// <summary>
        /// Processes the HTTP message asynchronously and injects token into query string to break CDN cache when changes are detected.
        /// This ensures fresh configuration data is retrieved when sentinel keys or collections have been modified.
        /// It also maintains eventual consistency across distributed instances by ensuring that the same token is used for all subsequent watch requests, until a new change is detected.
        /// </summary>
        /// <param name="message">The HTTP message.</param>
        /// <param name="pipeline">The pipeline.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public override async System.Threading.Tasks.ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            string token = _cdnTokenAccessor.Current;
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
