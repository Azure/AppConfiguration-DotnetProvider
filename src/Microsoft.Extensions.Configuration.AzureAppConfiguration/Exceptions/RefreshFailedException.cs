using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// An exception thrown when configuration refresh fails.
    /// </summary>
    public class RefreshFailedException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="RefreshFailedException"></see> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.</param>
        public RefreshFailedException(string message, Exception innerException) : base(message, innerException) { }
    }
}
