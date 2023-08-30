using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.DnsClient
{
    /// <summary>
    /// A <see cref="SrvRecord"/> representing a Srv record the server(s) for a specific protocol and domain.
    /// </summary>
    /// <seealso href="https://tools.ietf.org/html/rfc2782">RFC 2782</seealso>
    internal struct SrvRecord
    {
        /// <summary>
        /// The port on this target host of the SRV record
        /// </summary>
        public ushort Port { get; }

        /// <summary>
        /// The priority of the SRV record
        /// </summary>
        public ushort Priority { get; }

        /// <summary>
        /// The weight of the SRV record
        /// </summary>
        public ushort Weight { get; }

        /// <summary>
        /// The target host of the SRV recrod
        /// </summary>
        public string Target { get; }

        /// <summary>
        /// SRV record initializer.
        /// </summary>
        /// <param name="priority">The priority.</param>
        /// <param name="weight">The weight.</param>
        /// <param name="port">The port.</param>
        /// <param name="target">The target.</param>
        public SrvRecord(ushort priority, ushort weight, ushort port, string target)
        {
            Priority = priority;
            Weight = weight;
            Port = port;
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }
    }
}
