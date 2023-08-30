using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.DnsClient
{
    internal class DnsRequestIdMismatchException : Exception
    {
        public int RequestId { get; }

        public int ResponseId { get; }

        public DnsRequestIdMismatchException(int requestId, int responseId)
            : base()
        {
            RequestId = requestId;
            ResponseId = responseId;
        }
    }

    internal class DnsResponseException : Exception
    {
        /// <summary>
        /// Gets the response code.
        /// </summary>
        /// <value>
        /// The response code.
        /// </value>
        public DnsResponseCode Code { get; }

        /// <summary>
        /// Gets a human readable error message.
        /// </summary>
        /// <value>
        /// The error message.
        /// </value>
        public string DnsError { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DnsResponseException"/> class
        /// with <see cref="Code"/> set to <see cref="DnsResponseCode.Unassigned"/>
        /// and a custom <paramref name="message"/>.
        /// </summary>
        public DnsResponseException(string message) : base(message)
        {
            Code = DnsResponseCode.Unassigned;
            DnsError = DnsResponseCodeText.GetErrorText(Code);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DnsResponseException"/> class
        /// with the standard error text for the given <paramref name="code"/>.
        /// </summary>
        public DnsResponseException(DnsResponseCode code) : base(DnsResponseCodeText.GetErrorText(code))
        {
            Code = code;
            DnsError = DnsResponseCodeText.GetErrorText(Code);
        }
    }

    internal class DnsResponseTruncatedException : DnsResponseException
    {
        public DnsResponseTruncatedException()
            : base("Response is truncated")
        {

        }
    }
}
