using Azure.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.DnsClient
{
    internal class DnsTcpProcessor : DnsProcessor
    {
        public override async Task<IReadOnlyCollection<SrvRecord>> QueryAsync(IPEndPoint endpoint, string query, CancellationToken cancellationToken)
        {
            var tcpClient = new TcpClient(endpoint.AddressFamily);

            var resultRecords = new List<SrvRecord>();

            try
            {
                using var cancelCallback = cancellationToken.Register(() =>
                {
                    if (tcpClient == null)
                    {
                        return;
                    }
#if !NET45
                    tcpClient.Dispose();
#else
                    tcpClient.Close();
#endif
                });

                var originRecords = await QueryAsyncInternal(tcpClient, $"{OriginSrvPrefix}.{query}", cancellationToken).ConfigureAwait(false);
                string originHost = query;
                if (originRecords != null && originRecords.Count > 0)
                {
                    originHost = originRecords.First().Target;
                }

                IReadOnlyCollection<SrvRecord> altRecords = null;

                ushort index = 0;
                while (true)
                {
                    altRecords = await QueryAsyncInternal(tcpClient, $"{AlternativeSrvPrefix(index)}.{originHost}", cancellationToken).ConfigureAwait(false);

                    if (altRecords == null || altRecords.Count == 0)
                    {
                        break;
                    }

                    resultRecords.AddRange(altRecords);

                    index++;
                }
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.OperationAborted)
            {
                throw new TimeoutException();
            }
            catch (ObjectDisposedException)
            {
                // we disposed it in case of a timeout request, just indicate it actually timed out.
                throw new TimeoutException();
            }
            catch (DnsResponseException)
            {
                return resultRecords;
            }
            finally
            {
#if !NET45
                tcpClient.Dispose();
#else
                tcpClient.Close();
#endif
            }

            return resultRecords;
        }

        private async Task<IReadOnlyCollection<SrvRecord>> QueryAsyncInternal(TcpClient client, string query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stream = client.GetStream();

            ushort requestId = GetRandomRequestId();
            var srvRequest = BuildDnsQueryMessage(query, requestId);

            byte[] lengthPrefixedBytes = new byte[srvRequest.Length + 2];

            lengthPrefixedBytes[0] = (byte)((srvRequest.Length >> 8) & 0xff); // First byte of the length (high byte).
            lengthPrefixedBytes[1] = (byte)(srvRequest.Length & 0xff);

            srvRequest.CopyTo(lengthPrefixedBytes, 2);

            int retry = 0;
            while (true)
            {
                try 
                {
                    await stream.WriteAsync(lengthPrefixedBytes, 0, lengthPrefixedBytes.Length, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                    if (!stream.CanRead)
                    {
                        // might retry
                        throw new TimeoutException();
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    var lengthBuffer = new byte[2];
                    int bytesReceived = 0;
                    _ = await stream.ReadAsync(lengthBuffer, bytesReceived, 2, cancellationToken).ConfigureAwait(false);

                    var length = lengthBuffer[0] << 8 | lengthBuffer[1];

                    if (length <= 0)
                    {
                        // server signals close/disconnecting, might retry
                        throw new TimeoutException();
                    }

                    var contentBuffer = new byte[length];

                    var recievedLength = await stream.ReadAsync(contentBuffer, bytesReceived, length, cancellationToken).ConfigureAwait(false);

                    if (recievedLength <= 0)
                    {
                        // disconnected
                        throw new TimeoutException();
                    }

                    return ProcessDnsResponse(contentBuffer, requestId);
                }
                catch (DnsResponseException)
                {
                    // No need to retry with DnsResponseException
                    throw;
                }
                catch (Exception)
                {
                    if (retry == RetryAttempt)
                    {
                        throw;
                    }
                    retry++;
                }
            }
        }
    }
}
