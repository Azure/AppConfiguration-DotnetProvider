using Azure.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.DnsClient
{
    internal class SrvLookupClient
    {
        private readonly DnsProcessor _tcpProcessor;
        private readonly DnsProcessor _udpProcessor;
        private readonly Logger _logger;

        public SrvLookupClient(Logger logger)
        {
            _udpProcessor = new DnsUdpProcessor();
            _tcpProcessor = new DnsTcpProcessor();
            _logger = logger;
        }

        public async Task<IReadOnlyCollection<SrvRecord>> QueryAsync(string query, CancellationToken cancellationToken)
        {
            IReadOnlyCollection<NameServer> nameServers = null;
            try
            {
                nameServers = NameServer.ResolveNameServers();

                return await ResolveQueryAsync(nameServers, _udpProcessor, query, cancellationToken).ConfigureAwait(false);
            }
            catch (DnsResponseTruncatedException)
            {
                // Failover to TCP if UDP response is truncated
                try
                {
                    return await ResolveQueryAsync(nameServers, _tcpProcessor, query, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(LogHelper.QuerySrvDnsFailedErrorMessage(ex.Message));

                    return Enumerable.Empty<SrvRecord>().ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(LogHelper.QuerySrvDnsFailedErrorMessage(ex.Message));

                return Enumerable.Empty<SrvRecord>().ToArray();
            }
        }

        private async Task<IReadOnlyCollection<SrvRecord>> ResolveQueryAsync(
            IReadOnlyCollection<NameServer> servers,
            DnsProcessor processor,
            string query,
            CancellationToken cancellationToken)
        {
            if (servers == null)
            {
                throw new ArgumentNullException(nameof(servers));
            }
            if (processor == null)
            {
                throw new ArgumentNullException(nameof(processor));
            }
            if (string.IsNullOrEmpty(query))
            {
                throw new ArgumentNullException(nameof(query));
            }

            foreach (var server in servers)
            {
                return await processor.QueryAsync(
                     server.IPEndPoint,
                     query,
                     cancellationToken).ConfigureAwait(false);
            }
            
            return Enumerable.Empty<SrvRecord>().ToArray();
        }
    }
}
