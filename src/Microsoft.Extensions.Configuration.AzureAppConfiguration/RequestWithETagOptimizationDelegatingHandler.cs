using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Delegating Http Request Handler to optimize GET requests by matching resource's e-tags.
    /// </summary>
    public class RequestWithETagOptimizationDelegatingHandler : DelegatingHandler
    {
        public static string ETag { get; set; } = null;

        public static bool UseETag { get; set; } = false;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (UseETag)
            {
                if (!string.IsNullOrEmpty(ETag))
                {
                    request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, $"\"{ETag}\"");
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, "*");
                }
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (!UseETag)
            {
                return response;
            }
            else if (response.StatusCode == HttpStatusCode.NotModified || (ETag == null && response.StatusCode == HttpStatusCode.PreconditionFailed))
            {
                return response;
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw await GetResponseException(response);
        }

        private static async Task<Exception> GetResponseException(HttpResponseMessage response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            Exception responseException = null;
            string errorMessage = response.ReasonPhrase;
            string contentType = response.Content?.Headers.ContentType?.ToString();
            string content = null;

            //
            // Only parse expected content type
            if (contentType != null && contentType.Contains("application/problem+json"))
            {
                content = await response.Content.ReadAsStringAsync();

                try
                {
                    JObject error = JObject.Parse(content);
                    errorMessage = error.Value<string>("detail") ?? error.Value<string>("title") ?? errorMessage;
                }
                catch (JsonReaderException)
                {
                    // Server failed to return a parsible JSON error, swallow to prevent JSON parsing exception
                }
            }

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    responseException = new UnauthorizedAccessException(errorMessage);
                    break;
                case HttpStatusCode.Forbidden:
                    responseException = new InvalidOperationException(errorMessage);
                    break;
                case HttpStatusCode.MethodNotAllowed:
                    responseException = new NotSupportedException(errorMessage);
                    break;
                case HttpStatusCode.PreconditionFailed:
                    responseException = new ArgumentException(errorMessage);
                    break;
                case HttpStatusCode.NotModified:
                    responseException = new ArgumentException(errorMessage);
                    break;
                default:
                    responseException = new Exception(content ?? errorMessage);
                    break;
            }

            return responseException;
        }
    }
}
