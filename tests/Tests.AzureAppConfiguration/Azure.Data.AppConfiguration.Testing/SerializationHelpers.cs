﻿using Azure.Core;
using System.Text.Json;

namespace Azure.Data.AppConfiguration.Tests
{
    internal class SerializationHelpers
    {
        public delegate void SerializerFunc<in T>(ref Utf8JsonWriter writer, T t);

        public static byte[] Serialize<T>(T t, SerializerFunc<T> serializerFunc)
        {
            var writer = new ArrayBufferWriter<byte>();
            var json = new Utf8JsonWriter(writer);
            serializerFunc(ref json, t);
            json.Flush();
            return writer.WrittenMemory.ToArray();
        }
    }
}
