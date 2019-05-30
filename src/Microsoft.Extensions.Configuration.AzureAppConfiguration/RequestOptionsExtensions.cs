using System;
using System.Collections.Generic;
using Microsoft.Azure.AppConfiguration.Azconfig;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal static class RequestOptionsExtensions
    {
        public static IRequestOptions AddRequestType(this IRequestOptions requestOptions, RequestType requestType)
        {
            const string RequestTypeKey = "RequestType";
            string requestTypeValue = Enum.GetName(typeof(RequestType), requestType);

            requestOptions.CorrelationContext.Add(new KeyValuePair<string, string>(RequestTypeKey, requestTypeValue));

            return requestOptions;
        }
    }
}
