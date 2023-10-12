using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Exceptions
{
    internal class FallbackClientLookupException : Exception
    {
        public FallbackClientLookupException(Exception inner)
         : base(ErrorMessages.LookupFallbackClientFail, inner) { }
    }
}
