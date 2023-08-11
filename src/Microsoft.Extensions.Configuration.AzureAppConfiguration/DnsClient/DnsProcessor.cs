using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.DnsClient
{
    internal abstract class DnsProcessor
    {
        protected const ushort SrvType = 33;  // SRV DNS type code
        protected const ushort InClass = 1;  // IN DNS class code
        protected const ushort OptRrType = 41;  // OPT DNS type code
        protected const int RetryAttempt = 3;

        protected const string OriginSrvPrefix = "_origin._tcp";
        protected string AlternativeSrvPrefix(ushort index) => $"_alt{index}._tcp";

        public abstract Task<IReadOnlyCollection<SrvRecord>> QueryAsync(IPEndPoint endpoint, string query, CancellationToken cancellationToken);

        //
        // See RFC1035, RFC2782 and RFC6891
        protected byte[] BuildDnsQueryMessage(string query, ushort requestId)
        {
            using MemoryStream memoryStream = new MemoryStream();

            /* DNS header section
             *                                  1  1  1  1  1  1
                  0  1  2  3  4  5  6  7  8  9  0  1  2  3  4  5
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                      ID                       |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |QR|   Opcode  |AA|TC|RD|RA|   Z    |   RCODE   |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                    QDCOUNT                    |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                    ANCOUNT                    |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                    NSCOUNT                    |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                    ARCOUNT                    |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+

             */
            using BinaryWriter writer = new BinaryWriter(memoryStream);
            writer.Write(ConvertToNetworkOrder(requestId)); // Identifier 0xB8F5
            writer.Write(ConvertToNetworkOrder(0x0100)); // Flags, Recursion desired
            writer.Write(ConvertToNetworkOrder(0x0001)); // QDCOUNT
            writer.Write((ushort)0x0000); // ANCOUNT
            writer.Write((ushort)0x0000); // NSCOUNT
            writer.Write(ConvertToNetworkOrder(0x0001)); // ARCOUNT (OPT)

            /* SRV DNS question section
             * 
             *                                  1  1  1  1  1  1
                  0  1  2  3  4  5  6  7  8  9  0  1  2  3  4  5
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                                               |
                /                     QNAME                     /
                /                                               /
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                     QTYPE                     |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                     QCLASS                    |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
             */
            string[] labels = query.Split('.');
            foreach (string label in labels)
            {
                writer.Write((byte)label.Length);
                writer.Write(Encoding.ASCII.GetBytes(label));
            }
            writer.Write((byte)0x00); // End of QNAME

            writer.Write(ConvertToNetworkOrder(SrvType)); // QTYPE = SRV
            writer.Write(ConvertToNetworkOrder(InClass)); // QCLASS = IN

            // OPT pseudo-RR be added to the addional data section
            /*
             * +------------+--------------+------------------------------+
               | Field Name | Field Type   | Description                  |
               +------------+--------------+------------------------------+
               | NAME       | domain name  | MUST be 0 (root domain)      |
               | TYPE       | u_int16_t    | OPT (41)                     |
               | CLASS      | u_int16_t    | requestor's UDP payload size |
               | TTL        | u_int32_t    | extended RCODE and flags     |
               | RDLEN      | u_int16_t    | length of all RDATA          |
               | RDATA      | octet stream | {attribute,value} pairs      |
               +------------+--------------+------------------------------+
             */
            writer.Write((byte)0x00);
            writer.Write(ConvertToNetworkOrder(OptRrType)); // OPT RR type = 41
            writer.Write(ConvertToNetworkOrder(0x1000)); // UDP payload size = 4096 bytes, see RFC6891 6.2.5
            writer.Write((uint)0x0000); // Extended RCODE and flags
            writer.Write((ushort)0x0000); // No RDATA

            writer.Flush();
            return memoryStream.ToArray();
        }

        //
        // See RFC1035, RFC2782 and RFC6891
        protected IReadOnlyCollection<SrvRecord> ProcessDnsResponse(byte[] responseBuffer, ushort requestId)
        {
            var srvRecords = new List<SrvRecord>();

            // Check if the response is empty
            if (responseBuffer.Length < 12)
            {
                throw new DnsResponseException("Invalid DNS response");
            }

            // Extract the response header fields
            /*
             *                                  1  1  1  1  1  1
                  0  1  2  3  4  5  6  7  8  9  0  1  2  3  4  5
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                      ID                       |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |QR|   Opcode  |AA|TC|RD|RA|   Z    |   RCODE   |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                    QDCOUNT                    |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                    ANCOUNT                    |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                    NSCOUNT                    |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                    ARCOUNT                    |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+

             */
            ushort responseId = ConverToHostOrder(BitConverter.ToUInt16(responseBuffer, 0));

            if (responseId != requestId)
            {
                throw new DnsXidMismatchException(requestId, responseId);
            }

            ushort flags = ConverToHostOrder(BitConverter.ToUInt16(responseBuffer, 2));
            ushort answerCount = ConverToHostOrder(BitConverter.ToUInt16(responseBuffer, 6));

            // Check if the response is an error
            var rcode = flags & 0x000f; // Last 4 bits of the flags field
            if (rcode != 0)
            {
                throw new DnsResponseException((DnsResponseCode)rcode);
            }

            var isTruncated = (flags & 0x0200) != 0; // 7th bit of the flags field
            if (isTruncated)
            {
                throw new DnsResponseTruncatedException();
            }

            // Check if the response contains answers
            if (answerCount == 0)
            {
                return Enumerable.Empty<SrvRecord>().ToArray();
            }

            // Start parsing the DNS response to extract the SRV records
            int currentPosition = 12; // Start after the DNS header

            // Skip the name labels in the DNS response which should be end with a null byte
            while (responseBuffer[currentPosition] != 0)
            {
                currentPosition++;
            }

            currentPosition += 5; // Skip the label end, type, class fields

            /* Process each answer in the DNS response
             * 
             * Format of each answer
             *                                  1  1  1  1  1  1
                  0  1  2  3  4  5  6  7  8  9  0  1  2  3  4  5
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                                               |
                /                                               /
                /                      NAME                     /
                |                                               |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                      TYPE                     |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                     CLASS                     |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                      TTL                      |
                |                                               |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
                |                   RDLENGTH                    |
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--|
                /                     RDATA                     /
                /                                               /
                +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
             */

            for (int i = 0; i < answerCount; i++)
            {
                currentPosition += 2;// Skip the name, see rfc1035 4.1.4
                // Extract the answer type and class
                ushort answerType = ConverToHostOrder(BitConverter.ToUInt16(responseBuffer, currentPosition));
                ushort answerClass = ConverToHostOrder(BitConverter.ToUInt16(responseBuffer, currentPosition + 2));

                // Check if the answer is an SRV record (type 33) in the IN class (class 1)
                if (answerType == SrvType && answerClass == InClass)
                {
                    // Skip the type, class, and TTL fields to get to the data length field
                    currentPosition += 8;

                    // Extract the data length
                    ushort dataLength = ConverToHostOrder(BitConverter.ToUInt16(responseBuffer, currentPosition));

                    // Move to the start of the data section
                    currentPosition += 2;

                    // Extract the priority, weight, port, and target information
                    ushort priority = ConverToHostOrder(BitConverter.ToUInt16(responseBuffer, currentPosition));
                    ushort weight = ConverToHostOrder(BitConverter.ToUInt16(responseBuffer, currentPosition + 2));
                    ushort port = ConverToHostOrder(BitConverter.ToUInt16(responseBuffer, currentPosition + 4));
                    // Extract the target hostname
                    string target = ExtractHostname(responseBuffer, currentPosition + 6); // Skip the priority, weight, and port fields to get to the target hostname

                    srvRecords.Add(new SrvRecord(priority, weight, port, target));

                    // Move to the next answer
                    currentPosition += dataLength;
                }
                else
                {
                    // Skip the answer if it's not an SRV record
                    currentPosition += 10; // Skip the type, class, and TTL fields
                    ushort dataLength = ConverToHostOrder(BitConverter.ToUInt16(responseBuffer, currentPosition));
                    currentPosition += 2 + dataLength; // Skip the data length and data section
                }
            }

            return srvRecords;
        }

        protected string ExtractHostname(byte[] responseBuffer, int currentPosition)
        {
            var labels = new List<string>();

            // Count the length of the hostname
            while (responseBuffer[currentPosition] != 0)
            {
                int labelLength = responseBuffer[currentPosition];

                byte[] hostnameBytes = new byte[labelLength];
                Array.Copy(responseBuffer, currentPosition + 1, hostnameBytes, 0, labelLength);

                currentPosition += labelLength + 1;

                var label = Encoding.ASCII.GetString(hostnameBytes);
                labels.Add(label);
            }

            return string.Join(".", labels);
        }

        protected ushort ConverToHostOrder(ushort value)
        {
            return (ushort)(value << 8 | value >> 8);
        }

        protected ushort ConvertToNetworkOrder(ushort value)
        {
            return (ushort)(IPAddress.HostToNetworkOrder(value) >> 16);
        }

        protected ushort GetRandomRequestId()
        {
            return (ushort)new Random().Next(0, ushort.MaxValue);
        }
    }
}
