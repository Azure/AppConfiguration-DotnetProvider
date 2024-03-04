// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.FeatureManagement
{
    internal class JsonFlattener
    {
        private readonly List<KeyValuePair<string, string>> _data = new List<KeyValuePair<string, string>>();
        private readonly Stack<string> _context = new Stack<string>();
        private string _currentPath;

        public List<KeyValuePair<string, string>> FlattenJson(JsonElement rootElement)
        {
            VisitJsonElement(rootElement);
            return _data;
        }

        private void VisitJsonProperty(JsonProperty property)
        {
            EnterContext(property.Name);
            VisitJsonElement(property.Value);
            ExitContext();
        }

        private void VisitJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var enumeratedElement = element.EnumerateObject();

                    if (!enumeratedElement.Any())
                    {
                        _data.Add(new KeyValuePair<string, string>(_currentPath, null));
                    }
                    else
                    {
                        foreach (JsonProperty property in enumeratedElement)
                        {
                            VisitJsonProperty(property);
                        }
                    }

                    break;

                case JsonValueKind.Array:
                    int elementArrayLength = element.GetArrayLength();

                    if (elementArrayLength == 0)
                    {
                        _data.Add(new KeyValuePair<string, string>(_currentPath, null));
                    }
                    else
                    {
                        for (int index = 0; index < element.GetArrayLength(); index++)
                        {
                            EnterContext(index.ToString());
                            VisitJsonElement(element[index]);
                            ExitContext();
                        }
                    }

                    break;

                case JsonValueKind.String:
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    _data.Add(new KeyValuePair<string, string>(_currentPath, element.ToString()));
                    break;

                default:
                    break;
            }
        }

        private void EnterContext(string context)
        {
            _context.Push(context);
            _currentPath = ConfigurationPath.Combine(_context.Reverse());
        }

        private void ExitContext()
        {
            _context.Pop();
            _currentPath = ConfigurationPath.Combine(_context.Reverse());
        }
    }
}
