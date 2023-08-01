using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.DnsClient
{
    /// <summary>
    /// A <see cref="SrvRecord"/> representing a Srv record the server(s) for a specific protocol and domain.
    /// </summary>
    /// <seealso href="https://tools.ietf.org/html/rfc2782">RFC 2782</seealso>
    public struct SrvRecord
    {
        /// <summary>
        /// Gets the port.
        /// </summary>
        /// <value>
        /// The port.
        /// </value>
        public ushort Port { get; }

        /// <summary>
        /// Gets the priority.
        /// </summary>
        /// <value>
        /// The priority.
        /// </value>
        public ushort Priority { get; }

        /// <summary>
        /// Gets the target domain name.
        /// </summary>
        /// <value>
        /// The target.
        /// </value>
        public string Target { get; }

        /// <summary>
        /// Gets the weight.
        /// </summary>
        /// <value>
        /// The weight.
        /// </value>
        public ushort Weight { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SrvRecord" /> class.
        /// </summary>
        /// <param name="priority">The priority.</param>
        /// <param name="weight">The weight.</param>
        /// <param name="port">The port.</param>
        /// <param name="target">The target.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="target"/> is null.</exception>
        public SrvRecord(ushort priority, ushort weight, ushort port, string target)
        {
            Priority = priority;
            Weight = weight;
            Port = port;
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format(
                "{0} {1} {2} {3}",
                Priority,
                Weight,
                Port,
                Target);
        }
    }
}
