// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System;
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.SnapshotReferences
{
    /// <summary>
    /// Provides parsing functionality for snapshot reference configuration settings.
    /// </summary>
    internal static class SnapshotReferenceParser
    {
        /// <summary>
        /// Parses a snapshot name from a snapshot reference configuration setting.
        /// /// </summary>
        /// <param name="setting">The configuration setting containing the snapshot reference JSON.</param>
        /// <returns>The snapshot name if found and valid; otherwise, null.</returns>
        /// <exception cref="FormatException">Thrown when the setting contains invalid JSON or invalid snapshot reference format.</exception>
        public static string ParseSnapshotName(ConfigurationSetting setting)
        {
            if (setting == null || string.IsNullOrWhiteSpace(setting.Value))
            {
                return null;
            }

            try
            {
                Utf8JsonReader reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(setting.Value));

                // Ensure the JSON begins with an object '{'
                if (reader.Read() && reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new FormatException($"Invalid snapshot reference format. Expected JSON object but found {reader.TokenType}.");
                }

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    if (reader.GetString() == SnapshotReferenceConstants.SnapshotReferenceJsonPropertyName)
                    {
                        if (reader.Read() && reader.TokenType == JsonTokenType.String)
                        {
                            string snapshotName = reader.GetString();
                            if (!string.IsNullOrEmpty(snapshotName))
                            {
                                return snapshotName;
                            }
                        }
                        else
                        {
                            // Invalid snapshot name
                            throw new FormatException($"Invalid snapshot reference format. The 'snapshot_name' property must be a string value, but found {reader.TokenType}.");
                        }
                    }
                    else
                    {
                        // Skip unknown properties
                        reader.Skip();
                    }
                }

                return null; // Snapshot name property not found
            }
            catch (JsonException jsonEx)
            {
                throw new FormatException($"Invalid snapshot reference format. The value is not valid JSON.", jsonEx);
            }
        }
    }
}
