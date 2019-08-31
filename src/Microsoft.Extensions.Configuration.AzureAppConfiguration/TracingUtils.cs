namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Runtime.Versioning;
    using System.Text;

    internal static class TracingUtils
    {
        public static string GenerateUserAgent(string currentUserAgent = null)
        {
            Assembly assembly = typeof(AzureAppConfigurationOptions).Assembly;
            var userAgent = new StringBuilder($"{assembly.GetName().Name}/{assembly.GetName().Version}");

            //
            // If currentUserAgent is not null, prepend current assembly name and version to it,
            // and return without any further processing.
            if (!string.IsNullOrWhiteSpace(currentUserAgent))
            {
                 return $"{userAgent.ToString()} {currentUserAgent}";
            }

            IEnumerable<TargetFrameworkAttribute> targetFrameworkAttributes = assembly.GetCustomAttributes(true)?.OfType<TargetFrameworkAttribute>();
            if (targetFrameworkAttributes != null && targetFrameworkAttributes.Any())
            {
                var frameworkName = new FrameworkName(targetFrameworkAttributes.First().FrameworkName);
                userAgent.Append($" {frameworkName.Identifier}/{frameworkName.Version}");
            }

            string comment = RuntimeInformation.OSDescription;
            if (!string.IsNullOrEmpty(comment))
            {
                userAgent.Append($" ({comment})");
            }

            return userAgent.ToString();
        }

        public static void ConfigureRequestTracing(bool tracingEnabled, RequestType requestType, HostType hostType)
        {
            IList<KeyValuePair<string, string>> correlationContext = new List<KeyValuePair<string, string>>();

            if (tracingEnabled)
            {
                AddRequestType(correlationContext, requestType);

                if (hostType != HostType.None)
                {
                    AddHostType(correlationContext, hostType);
                }
            }

            // TODO: use correlationContext to set headers
        }

        private static void AddRequestType(IList<KeyValuePair<string, string>> correlationContext, RequestType requestType)
        {
            string requestTypeValue = Enum.GetName(typeof(RequestType), requestType);
            correlationContext.Add(new KeyValuePair<string, string>(RequestTracingConstants.RequestTypeKey, requestTypeValue));
        }

        private static void AddHostType(IList<KeyValuePair<string, string>> correlationContext, HostType hostType)
        {
            string hostTypeValue = Enum.GetName(typeof(HostType), hostType);
            correlationContext.Add(new KeyValuePair<string, string>(RequestTracingConstants.HostTypeKey, hostTypeValue));
        }
    }
}
