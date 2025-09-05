// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System;
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.SnapshotReference
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
                throw new FormatException(string.Format(ErrorMessages.SnapshotReferenceInvalidFormat, setting.Key, setting.Label));
            }

            try
            {
                var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(setting.Value));

                if (reader.Read() && reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new FormatException(string.Format(ErrorMessages.SnapshotReferenceInvalidFormat, setting.Key, setting.Label));
                }

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    if (reader.GetString() == JsonFields.SnapshotName)
                    {
                        if (reader.Read() && reader.TokenType == JsonTokenType.String)
                        {
                            if (string.IsNullOrWhiteSpace(reader.GetString()))
                            {
                                throw new FormatException(string.Format(ErrorMessages.SnapshotReferenceInvalidFormat, setting.Key, setting.Label));
                            }

                            return new SnapshotReference { SnapshotName = reader.GetString() };
                        }

                        throw new FormatException(string.Format(ErrorMessages.SnapshotReferenceInvalidJsonProperty, setting.Key, setting.Label, reader.TokenType));
                    }

                    // Skip unknown properties
                    reader.Skip();
                }

                throw new FormatException(string.Format(ErrorMessages.SnapshotReferencePropertyMissing, setting.Key, setting.Label));
            }
            catch (JsonException jsonEx)
            {
                throw new FormatException(string.Format(ErrorMessages.SnapshotReferenceInvalidFormat, setting.Key, setting.Label), jsonEx);
            }
        }
    }
}
