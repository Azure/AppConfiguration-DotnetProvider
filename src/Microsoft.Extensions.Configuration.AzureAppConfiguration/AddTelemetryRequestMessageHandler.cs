using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Delegated request message handler to add telemetry info to request headers.
    /// </summary>
    class AddTelemetryRequestMessageHandler : DelegatingHandler
    {
        /// <summary>
        /// Header name to send telemetry info in.
        /// </summary>
        private const string CorrelationContextHeader = "Correlation-Context";

        /// <summary>
        /// Field name which specifies if server should log the type of request or not.
        /// </summary>
        private const string LogRequestTypeFieldName = "log-request-type";

        /// <summary>
        /// Value of the field which specifies if the server should log the type of request.
        /// </summary>
        public bool LogRequestType { get; set; } = true;

        /// <summary>
        /// Type of request.
        /// </summary>
        public RequestType RequestType { get; set; } = RequestType.None;

        /// <summary>
        /// Override the SendAsync method to add headers before calling the base.SendAsync.
        /// </summary>
        /// <param name="request">The request object.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An asynchronous response object.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            //
            // Add request type to telemetry header.
            AddCorrelationContextHeader(request, Enum.GetName(typeof(RequestType), this.RequestType), this.RequestType.ToString());
            AddCorrelationContextHeader(request, LogRequestTypeFieldName, LogRequestType.ToString());

            return await base.SendAsync(request, cancellationToken);
        }

        /// <summary>
        /// Add a given "field=value" to Correlation-Context header.
        /// </summary>
        /// <param name="request">The request to be processed.</param>
        /// <param name="field">The field name.</param>
        /// <param name="value">The value.</param>
        public static void AddCorrelationContextHeader(HttpRequestMessage request, string field, string value)
        {
            string headerValue = $"{field}={value}";
            if (request.Headers.Contains(CorrelationContextHeader))
            {
                headerValue = $"{string.Join(",", request.Headers.GetValues(CorrelationContextHeader))},{headerValue}";
                request.Headers.Remove(CorrelationContextHeader);
            }

            request.Headers.Add(CorrelationContextHeader, headerValue);
        }
    }
}
