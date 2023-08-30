using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System;
using System.Linq;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.DnsClient
{
    internal class NameServer
    {
        /// <summary>
        /// The default DNS server port.
        /// </summary>
        public const int DefaultPort = 53;

        /// <summary>
        /// Initializes a new instance of the <see cref="NameServer"/> class.
        /// </summary>
        /// <param name="endPoint">The name server endpoint.</param>
        internal NameServer(IPEndPoint endPoint)
        {
            IPEndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="NameServer"/> class.
        /// </summary>
        /// <param name="endPoint">The name server endpoint.</param>
        /// <param name="port">The name server port.</param>
        /// <param name="dnsSuffix">An optional DNS suffix (can be null).</param>
        internal NameServer(IPAddress endPoint, int port, string dnsSuffix)
            : this(new IPEndPoint(endPoint, port), dnsSuffix)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NameServer"/> class.
        /// </summary>
        /// <param name="endPoint">The name server endpoint.</param>
        /// <param name="dnsSuffix">An optional DNS suffix (can be null).</param>
        internal NameServer(IPEndPoint endPoint, string dnsSuffix)
            : this(endPoint)
        {
            DnsSuffix = string.IsNullOrWhiteSpace(dnsSuffix) ? null : dnsSuffix;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NameServer"/> class from a <see cref="IPEndPoint"/>.
        /// </summary>
        /// <param name="endPoint">The endpoint.</param>
        public static implicit operator NameServer(IPEndPoint endPoint)
        {
            if (endPoint == null)
            {
                return null;
            }

            return new NameServer(endPoint);
        }

        internal IPEndPoint IPEndPoint { get; }

        /// <summary>
        /// Gets an optional DNS suffix which a resolver can use to append to queries or to find servers suitable for a query.
        /// </summary>
        internal string DnsSuffix { get; }


        /// <summary>
        /// Gets a list of name servers by iterating over the available network interfaces.
        /// </summary>
        /// <returns>
        /// The list of name servers.
        /// </returns>
        internal static IReadOnlyCollection<NameServer> ResolveNameServers()
        {
            IReadOnlyCollection<NameServer> nameServers = new NameServer[0];

            var exceptions = new List<Exception>();

            nameServers = QueryNetworkInterfaces();

            IReadOnlyCollection<NameServer> filtered = nameServers
                .Where(p => (p.IPEndPoint.Address.AddressFamily == AddressFamily.InterNetwork
                            || p.IPEndPoint.Address.AddressFamily == AddressFamily.InterNetworkV6)
                            && !p.IPEndPoint.Address.IsIPv6SiteLocal)
                .ToArray();

            try
            {
                filtered = ValidateNameServers(filtered);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }

            if (filtered.Count == 0 && exceptions.Count > 0)
            {
                throw new InvalidOperationException("Could not resolve any NameServers.", exceptions.First());
            }

            return filtered;
        }

        internal static IReadOnlyCollection<NameServer> ValidateNameServers(IReadOnlyCollection<NameServer> servers)
        {
            // Right now, I'm only checking for ANY address, but might be more validation rules at some point...
            var validServers = servers.Where(p => !p.IPEndPoint.Address.Equals(IPAddress.Any) && !p.IPEndPoint.Address.Equals(IPAddress.IPv6Any)).ToArray();

            if (validServers.Length != servers.Count)
            {
                if (validServers.Length == 0)
                {
                    throw new InvalidOperationException("Unsupported ANY address cannot be used as name server and no other servers are configured to fall back to.");
                }
            }

            return validServers;
        }

        private static IReadOnlyCollection<NameServer> QueryNetworkInterfaces()
        {
            var result = new HashSet<NameServer>();

            var adapters = NetworkInterface.GetAllNetworkInterfaces();
            if (adapters == null)
            {
                return result.ToArray();
            }

            foreach (NetworkInterface networkInterface in
                adapters
                    .Where(p => p != null && (p.OperationalStatus == OperationalStatus.Up || p.OperationalStatus == OperationalStatus.Unknown)
                    && p.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var properties = networkInterface?.GetIPProperties();

                if (properties?.DnsAddresses == null)
                {
                    continue;
                }

                foreach (var ip in properties.DnsAddresses)
                {
                    result.Add(new NameServer(ip, DefaultPort, properties.DnsSuffix));
                }
            }

            return result.ToArray();
        }
    }
}