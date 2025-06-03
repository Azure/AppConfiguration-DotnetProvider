using Azure.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Cdn
{
    /// <summary>
    /// A token credential that provides an empty token.
    /// </summary>
    internal class EmptyTokenCredential : TokenCredential
    {
        /// <summary>
        /// Gets an empty token.
        /// </summary>
        /// <param name="requestContext">The context of the token request.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>An empty access token.</returns>
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new AccessToken(string.Empty, DateTimeOffset.MaxValue);
        }

        /// <summary>
        /// Asynchronously gets an empty token.
        /// </summary>
        /// <param name="requestContext">The context of the token request.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an empty access token.</returns>
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new ValueTask<AccessToken>(new AccessToken(string.Empty, DateTimeOffset.MaxValue));
        }
    }
}