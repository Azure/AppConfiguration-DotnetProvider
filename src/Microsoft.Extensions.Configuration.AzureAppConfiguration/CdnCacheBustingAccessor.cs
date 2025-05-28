// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System.Threading;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Implementation of ICdnCacheBustingAccessor that uses AsyncLocal for thread-safe context management.
    /// </summary>
    internal class CdnCacheBustingAccessor : ICdnCacheBustingAccessor
    {
        private static readonly AsyncLocal<CdnCacheBustingContext> _context = new AsyncLocal<CdnCacheBustingContext>();

        /// <summary>
        /// Gets or sets the current ETag value to be used for cache busting.
        /// When null, CDN cache busting is disabled. When not null, the ETag will be injected into requests.
        /// </summary>
        public string CurrentETag
        {
            get => _context.Value?.ETag;
            set => EnsureContext().ETag = value;
        }

        private static CdnCacheBustingContext EnsureContext()
        {
            return _context.Value ??= new CdnCacheBustingContext();
        }
    }

    /// <summary>
    /// Context class that holds the CDN cache busting state.
    /// </summary>
    internal class CdnCacheBustingContext
    {
        /// <summary>
        /// Gets or sets the ETag value for cache busting.
        /// </summary>
        public string ETag { get; set; }
    }
}
