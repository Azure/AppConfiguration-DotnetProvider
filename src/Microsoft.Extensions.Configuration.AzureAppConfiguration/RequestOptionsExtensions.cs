using System;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class RequestOptionsExtensions
    {
        public static IRequestOptions AddRequestType(this IRequestOptions requestOptions, RequestTypes requestType)
        {
            if (requestType != RequestTypes.None)
            {
                const string RequestTypeKey = "RequestType";
                string requestTypeValue = Enum.GetName(typeof(RequestTypes), requestType);

                requestOptions.CorrelationContext.Add(Tuple.Create<string, string>(RequestTypeKey, requestTypeValue));
            }

            return requestOptions;
        }
    }
}
