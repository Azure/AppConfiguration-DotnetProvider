using System;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class RequestOptionsExtensions
    {
        public static IRequestOptions AddRequestType(this IRequestOptions requestOptions, RequestType requestType)
        {
            if (requestType != RequestType.None)
            {
                const string RequestTypeKey = "RequestType";
                string requestTypeValue = Enum.GetName(typeof(RequestType), requestType);

                requestOptions.CorrelationContext.Add(Tuple.Create<string, string>(RequestTypeKey, requestTypeValue));
            }

            return requestOptions;
        }
    }
}
