// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.AspNetCore.Http;
using Microsoft.FeatureManagement.FeatureFilters;
using System;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Examples.ConfigStoreDemo
{
    /// <summary>
    /// Provides a stable anonymous visitor identity for feature variant allocation.
    /// </summary>
    public sealed class VisitorTargetingContextAccessor : ITargetingContextAccessor
    {
        private const string CookieName = "ConfigStoreDemo.VisitorId";
        private const string MissingHttpContextMessage = "Visitor targeting requires an active HTTP request.";
        private static readonly object ContextKey = new object();
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// Initializes the accessor with the current HTTP context.
        /// </summary>
        public VisitorTargetingContextAccessor(IHttpContextAccessor httpContextAccessor)
        {
            if (httpContextAccessor == null)
            {
                throw new ArgumentNullException(nameof(httpContextAccessor));
            }

            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Gets the request's targeting context, creating a visitor cookie when needed.
        /// </summary>
        public ValueTask<TargetingContext> GetContextAsync()
        {
            HttpContext httpContext = _httpContextAccessor.HttpContext;

            if (httpContext == null)
            {
                throw new InvalidOperationException(MissingHttpContextMessage);
            }

            if (httpContext.Items.TryGetValue(ContextKey, out object cachedContext))
            {
                return new ValueTask<TargetingContext>((TargetingContext)cachedContext);
            }

            string visitorId = httpContext.Request.Cookies[CookieName];

            if (!Guid.TryParseExact(visitorId, "N", out Guid parsedVisitorId))
            {
                parsedVisitorId = Guid.NewGuid();
                httpContext.Response.Cookies.Append(CookieName, parsedVisitorId.ToString("N"), new CookieOptions
                {
                    HttpOnly = true,
                    Secure = httpContext.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                    MaxAge = TimeSpan.FromDays(365)
                });
            }

            var targetingContext = new TargetingContext
            {
                UserId = parsedVisitorId.ToString("N"),
                Groups = Array.Empty<string>()
            };

            httpContext.Items[ContextKey] = targetingContext;
            return new ValueTask<TargetingContext>(targetingContext);
        }
    }
}
