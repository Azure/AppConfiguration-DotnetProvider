using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class SrvLookupClient
    {
        private class OriginHostCacheItem
        {
            public string OriginHost { get; set; }

            public DateTimeOffset CacheExpires { get; set; }
        }

        private readonly ConcurrentDictionary<string, OriginHostCacheItem> _cachedOriginHosts;

        private readonly LookupClient _tcpLookupClient;
        private readonly LookupClient _udpLookupClient;

        const string TcpOrigin = "_origin._tcp";
        const string TCP = "_tcp";
        const string Alt = "_alt";
        const int MaxSrvRecordCountPerRecordSet = 20;

        private readonly Logger _logger;

        public SrvLookupClient(Logger logger)
        {
            _cachedOriginHosts = new ConcurrentDictionary<string, OriginHostCacheItem>();

            _udpLookupClient = new LookupClient(new LookupClientOptions()
            {
                UseTcpFallback = false
            });

            _tcpLookupClient = new LookupClient(new LookupClientOptions()
            {
                UseTcpOnly = true
            });

            _logger = logger;
        }

        public async Task<IEnumerable<SrvRecord>> QueryAsync(string host, CancellationToken cancellationToken)
        {
            IEnumerable<SrvRecord> resultRecords = Enumerable.Empty<SrvRecord>();
            var originSrvDns = $"{TcpOrigin}.{host}";
            
            bool exists = _cachedOriginHosts.TryGetValue("originSrvDns", out OriginHostCacheItem originHost);

            try
            {
                if (!exists || originHost.CacheExpires <= DateTimeOffset.UtcNow)
                {
                    var records = await InternalQueryAsync(originSrvDns, cancellationToken).ConfigureAwait(false);

                    if (records == null || records.Count() == 0)
                    {
                        return Enumerable.Empty<SrvRecord>();
                    }

                    if (!exists)
                    {
                        originHost = new OriginHostCacheItem()
                        {
                            OriginHost = records.First().Target.Value.Trim('.'),
                            CacheExpires = DateTimeOffset.UtcNow.AddMinutes(30)
                        };

                        _cachedOriginHosts[originSrvDns] = originHost;
                    }
                    else
                    {
                        originHost.OriginHost = records.First().Target.Value.Trim('.');
                        originHost.CacheExpires = DateTimeOffset.UtcNow.AddMinutes(30);
                    }
                }

                int index = 0;

                while (true)
                {
                    string altSrvDns = $"{TcpAlternative(index)}.{originHost.OriginHost}";

                    var records = await InternalQueryAsync(altSrvDns, cancellationToken).ConfigureAwait(false);
                    resultRecords = resultRecords.Concat(records);

                    if (records.Count() < MaxSrvRecordCountPerRecordSet)
                    {
                        break;
                    }

                    index++;
                }

                return resultRecords;
            }
            catch (Exception e)
            {
                // Swallow all exceptions and return empty list
                _logger.LogWarning($"Exception while performing auto failover SRV DNS lookup: {e.Message}");

                return resultRecords;
            }
        }

        private async Task<IEnumerable<SrvRecord>> InternalQueryAsync(string srvDns, CancellationToken cancellationToken)
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

            IDnsQueryResponse dnsResponse;

            try
            {
                dnsResponse = await _udpLookupClient.QueryAsync(srvDns, QueryType.SRV, QueryClass.IN, linkedCts.Token).ConfigureAwait(false);

                return dnsResponse.Answers.SrvRecords();
            }
            catch (Exception e) when (e is DnsResponseException || e is OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                dnsResponse = await _tcpLookupClient.QueryAsync(srvDns, QueryType.SRV, QueryClass.IN, cancellationToken).ConfigureAwait(false);

                return dnsResponse.Answers.SrvRecords();
            }
        }

        private static string TcpAlternative(int index)
        {
            return $"{Alt}{index}.{TCP}";
        }
    }
}
