using Microsoft.Azure.AppConfiguration.Azconfig;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions
{
    internal static class RequestOptionsExtensions
    {
        public static void ConfigureRequestTracing(this IRequestOptions options, bool requestTracingEnabled, RequestType requestType, HostType hostType = HostType.None)
        {
            if (options != null && requestTracingEnabled)
            {
                options.AddRequestType(requestType);

                if (hostType != HostType.None)
                {
                    options.AddHostType(hostType);
                }
            }
        }

        private static IRequestOptions AddRequestType(this IRequestOptions requestOptions, RequestType requestType)
        {
            string requestTypeValue = Enum.GetName(typeof(RequestType), requestType);
            requestOptions.CorrelationContext.Add(new KeyValuePair<string, string>(RequestTracingConstants.RequestTypeKey, requestTypeValue));

            return requestOptions;
        }

        private static IRequestOptions AddHostType(this IRequestOptions requestOptions, HostType hostType)
        {
            string hostTypeValue = Enum.GetName(typeof(HostType), hostType);
            requestOptions.CorrelationContext.Add(new KeyValuePair<string, string>(RequestTracingConstants.HostTypeKey, hostTypeValue));

            return requestOptions;
        }
    }
}
