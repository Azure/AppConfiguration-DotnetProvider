namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
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
            // and return any further processing.
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
    }
}
