using System;
using System.Collections.Generic;
using System.Linq;
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
            catch (Exception ex) when (ex is TimeoutException || ex is DnsResponseTruncatedException)
            {
                // Failover to TCP if UDP response is truncated
                _logger.LogDebug(LogHelper.FailoverDnsLookupToTcp(ex.Message));

                try
                {
                    return await ResolveQueryAsync(nameServers, _tcpProcessor, query, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(LogHelper.QuerySrvDnsFailedErrorMessage(e.Message));

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
            if (servers.Count == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(servers));
            }

            var enumerator = servers.GetEnumerator();
            enumerator.MoveNext();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await processor.QueryAsync(
                         enumerator.Current.IPEndPoint,
                         query,
                         cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    if (!enumerator.MoveNext())
                    {
                        throw;
                    }
                }
            }
        }
    }
}
