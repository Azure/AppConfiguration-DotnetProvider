using System;
using System.Collections.Generic;
using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;

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

        public static IRequestOptions AddHostType(this IRequestOptions requestOptions, HostType hostType)
        {
            string hostTypeValue = Enum.GetName(typeof(HostType), hostType);
            requestOptions.CorrelationContext.Add(new KeyValuePair<string, string>(RequestTracingConstants.HostTypeKey, hostTypeValue));

            return requestOptions;
        }
    }
}
