// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal static class JsonElementExtensions
    {
        public static string SerializeWithSortedKeys(this JsonElement rootElement)
        {
            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                WriteElementWithSortedKeys(rootElement, writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteElementWithSortedKeys(JsonElement element, Utf8JsonWriter writer)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();

                    foreach (JsonProperty property in element.EnumerateObject().OrderBy(p => p.Name))
                    {
                        writer.WritePropertyName(property.Name);
                        WriteElementWithSortedKeys(property.Value, writer);
                    }

                    writer.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();

                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        WriteElementWithSortedKeys(item, writer);
                    }

                    writer.WriteEndArray();
                    break;

                case JsonValueKind.String:
                    writer.WriteStringValue(element.GetString());
                    break;

                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                    {
                        writer.WriteNumberValue(intValue);
                    }
                    else if (element.TryGetInt64(out long longValue))
                    {
                        writer.WriteNumberValue(longValue);
                    }
                    else if (element.TryGetDecimal(out decimal decimalValue))
                    {
                        writer.WriteNumberValue(element.GetDecimal());
                    }
                    else if (element.TryGetDouble(out double doubleValue))
                    {
                        writer.WriteNumberValue(element.GetDouble());
                    }

                    break;

                case JsonValueKind.True:
                    writer.WriteBooleanValue(true);
                    break;

                case JsonValueKind.False:
                    writer.WriteBooleanValue(false);
                    break;

                case JsonValueKind.Null:
                    writer.WriteNullValue();
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported JsonValueKind: {element.ValueKind}");
            }
        }
    }
}
