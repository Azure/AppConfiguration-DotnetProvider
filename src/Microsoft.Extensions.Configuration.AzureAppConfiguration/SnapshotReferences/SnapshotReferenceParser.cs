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
        /// </summary>
        /// <param name="setting">The configuration setting containing the snapshot reference JSON.</param>
        /// <returns>The snapshot reference if found and valid; otherwise, null.</returns>
        /// <exception cref="FormatException">Thrown when the setting contains invalid JSON or invalid snapshot reference format.</exception>
        public static SnapshotReference Parse(ConfigurationSetting setting)
        {
            if (setting == null)
            {
                throw new ArgumentNullException(nameof(setting));
            }

            if (string.IsNullOrWhiteSpace(setting.Value))
            {
                return null;
            }

            try
            {
                var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(setting.Value));

                if (reader.Read() && reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new FormatException(string.Format(ErrorMessages.SnapshotReferenceInvalidFormat, setting.Key, setting.Label, reader.TokenType));
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
                            return new SnapshotReference { SnapshotName = reader.GetString() };
                        }
                        else
                        {
                            throw new FormatException(string.Format(ErrorMessages.SnapshotReferenceInvalidJsonProperty, setting.Key, setting.Label, reader.TokenType));
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
                throw new FormatException(string.Format(ErrorMessages.SnapshotReferenceInvalidJson, setting.Key, setting.Label), jsonEx);
            }
        }
    }
}
