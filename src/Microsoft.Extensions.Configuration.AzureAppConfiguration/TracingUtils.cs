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
        public static string GenerateUserAgent()
        {
            Assembly assembly = typeof(AzureAppConfigurationOptions).Assembly;
            var userAgent = new StringBuilder($"{assembly.GetName().Name}/{assembly.GetName().Version}");
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
