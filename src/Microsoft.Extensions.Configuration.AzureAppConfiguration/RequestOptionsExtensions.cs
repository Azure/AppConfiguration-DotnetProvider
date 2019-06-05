using System;
using System.Collections.Generic;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class RequestOptionsExtensions
    {
        public static IRequestOptions AddRequestType(this IRequestOptions requestOptions, RequestType requestType)
        {
            string requestTypeValue = Enum.GetName(typeof(RequestType), requestType);
            requestOptions.CorrelationContext.Add(new KeyValuePair<string, string>(RequestTracingConstants.RequestTypeKey, requestTypeValue));

            return requestOptions;
        }
    }
}
