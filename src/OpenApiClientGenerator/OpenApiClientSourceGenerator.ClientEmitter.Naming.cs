using System.Text;

using Microsoft.OpenApi;

namespace OpenApiClientGenerator;

public sealed partial class OpenApiClientSourceGenerator
{
    private sealed partial class ClientEmitter
    {
        private static string MakeNullableTypeName(string typeName)
        {
            return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName : $"{typeName}?";
        }

        private static List<IOpenApiParameter> CollectParameters(IOpenApiPathItem pathItem, OpenApiOperation operation)
        {
            var pathParameters = pathItem.Parameters;
            var operationParameters = operation.Parameters;

            if ((pathParameters is null || pathParameters.Count == 0)
                && (operationParameters is null || operationParameters.Count == 0))
            {
                return [];
            }

            var parameters = new List<IOpenApiParameter>((pathParameters?.Count ?? 0) + (operationParameters?.Count ?? 0));
            if (pathParameters is not null)
            {
                parameters.AddRange(pathParameters);
            }

            if (operationParameters is not null)
            {
                parameters.AddRange(operationParameters);
            }

            return parameters;
        }

        private static KeyValuePair<string, T> SelectPreferredContent<T>(IEnumerable<KeyValuePair<string, T>> content, Func<KeyValuePair<string, T>, int> getPriority)
        {
            using var enumerator = content.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                throw new InvalidOperationException("The content collection must not be empty.");
            }

            var selected = enumerator.Current;
            var bestPriority = getPriority(selected);

            while (enumerator.MoveNext())
            {
                var candidate = enumerator.Current;
                var priority = getPriority(candidate);
                if (priority < bestPriority)
                {
                    selected = candidate;
                    bestPriority = priority;
                }
            }

            return selected;
        }

        private static string SafeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "value";
            }

            var builder = new StringBuilder(value.Length + 1);
            foreach (var ch in value)
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
            }

            var sanitized = builder.ToString().Trim('_');
            if (string.IsNullOrEmpty(sanitized))
            {
                sanitized = "value";
            }

            if (!IsIdentifierStartCharacter(sanitized[0]))
            {
                sanitized = "_" + sanitized;
            }

            return s_reservedIdentifiers.Contains(sanitized) ? $"@{sanitized}" : sanitized;
        }

        private static string ToPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                normalized.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }

            var parts = normalized.ToString().Split([' '], StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(static part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }

        private static string ToCamelCase(string value)
        {
            var pascal = ToPascalCase(value);
            return string.IsNullOrEmpty(pascal) ? "value" : char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        }

        private static bool IsReferenceLikeType(string typeName)
        {
            return typeName is "string"
                || typeName.StartsWith("IReadOnlyList<", StringComparison.Ordinal)
                || (!typeName.EndsWith("?", StringComparison.Ordinal) && typeName is not "int" and not "long" and not "float" and not "double" and not "bool" and not "DateOnly" and not "DateTimeOffset" and not "Guid" and not "JsonElement");
        }

        private static bool RequiresNonNullJsonResponse(string typeName)
        {
            return !typeName.EndsWith("?", StringComparison.Ordinal) && IsReferenceLikeType(typeName);
        }

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static SecuritySchemeBinding? CreateSecuritySchemeBinding(string schemeKey, IOpenApiSecurityScheme scheme)
        {
            if (scheme.Type == SecuritySchemeType.OAuth2
                || (scheme.Type == SecuritySchemeType.Http && string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase)))
            {
                var parameterName = SafeIdentifier(ToCamelCase(schemeKey == "oauth2" ? "access_token" : $"{schemeKey}_token"));
                return new SecuritySchemeBinding(
                    parameterName,
                    $"string? {parameterName} = default",
                    "Authorization",
                    SecuritySchemeLocation.Header,
                    isBearerToken: true);
            }

            if (scheme.Type == SecuritySchemeType.ApiKey)
            {
                var parameterName = SafeIdentifier(ToCamelCase($"{schemeKey}_api_key"));
                return new SecuritySchemeBinding(
                    parameterName,
                    $"string? {parameterName} = default",
                    scheme.Name ?? parameterName,
                    MapLocation(scheme.In ?? ParameterLocation.Header),
                    isBearerToken: false);
            }

            return null;
        }

        private static SecuritySchemeLocation MapLocation(ParameterLocation location)
        {
            return location switch
            {
                ParameterLocation.Query => SecuritySchemeLocation.Query,
                ParameterLocation.Cookie => SecuritySchemeLocation.Cookie,
                _ => SecuritySchemeLocation.Header,
            };
        }

        private static void EmitSecuritySchemeInitialization(StringBuilder builder, SecuritySchemeBinding securityScheme)
        {
            if (securityScheme.Location == SecuritySchemeLocation.Query)
            {
                builder.Append("        ").Append(securityScheme.FieldName).Append(" = ").Append(securityScheme.ParameterName).AppendLine(";");
                return;
            }

            builder.Append("        if (").Append(securityScheme.ParameterName).AppendLine(" is not null)");
            builder.AppendLine("        {");
            if (securityScheme.IsBearerToken)
            {
                builder.Append("            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", ").Append(securityScheme.ParameterName).AppendLine(");");
            }
            else if (securityScheme.Location == SecuritySchemeLocation.Cookie)
            {
                builder.Append("            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(\"Cookie\", \"").Append(securityScheme.HeaderOrParameterName).Append("=\" + ").Append(securityScheme.ParameterName).AppendLine(");");
            }
            else
            {
                builder.Append("            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(\"").Append(securityScheme.HeaderOrParameterName).Append("\", ").Append(securityScheme.ParameterName).AppendLine(");");
            }

            builder.AppendLine("        }");
        }

        private static string BuildOperationMethodName(string? operationId, string operationType, string route, string? tagName)
        {
            var source = operationId ?? $"{operationType}_{route}";
            var operationTokens = TokenizeWords(source);
            var tagTokens = new HashSet<string>(
                TokenizeWords(tagName ?? string.Empty)
                    .Select(NormalizeToken)
                    .Where(static token => token.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            var filteredTokens = operationTokens
                .Where(token => !tagTokens.Contains(NormalizeToken(token)))
                .ToList();

            if (filteredTokens.Count == 0)
            {
                filteredTokens.Add(operationType.ToLowerInvariant());
            }

            return SafeIdentifier(string.Concat(filteredTokens.Select(static token => ToPascalCase(token ?? string.Empty))));
        }

        private static List<string> TokenizeWords(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return [];
            }

            var normalized = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                normalized.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }

            return [.. normalized.ToString().Split([' '], StringSplitOptions.RemoveEmptyEntries)];
        }

        private static string NormalizeToken(string value)
        {
            var normalized = value.Trim();
            if (normalized.Length > 2
                && normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith("ss", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith("us", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized;
        }

        private static string? GetTagName(OpenApiOperation operation)
        {
            return operation.Tags?.FirstOrDefault()?.Name;
        }

        private static string BuildClientName(string documentPath, OpenApiDocument document)
        {
            var baseName = Path.GetFileNameWithoutExtension(documentPath);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = !string.IsNullOrWhiteSpace(document.Info.Title)
                    ? document.Info.Title
                    : "OpenApi";
            }

            var normalized = SafeIdentifier(ToPascalCase(baseName ?? "OpenApi"));
            return normalized.EndsWith("Client", StringComparison.Ordinal) ? normalized : $"{normalized}Client";
        }

        private static bool IsIdentifierStartCharacter(char ch)
        {
            return ch == '_' || char.IsLetter(ch);
        }

        private static bool HasSchemaType(IOpenApiSchema? schema, JsonSchemaType type)
        {
            return schema?.Type is { } schemaType && (schemaType & type) == type;
        }

        private static bool IsNullableSchema(IOpenApiSchema schema)
        {
            return HasSchemaType(schema, JsonSchemaType.Null);
        }

        private static bool IsNullOnlySchema(IOpenApiSchema? schema)
        {
            return schema is not null
                && schema.Type == JsonSchemaType.Null
                && string.IsNullOrWhiteSpace(schema.Format)
                && (schema.Properties?.Count ?? 0) == 0
                && (schema.AllOf?.Count ?? 0) == 0
                && (schema.AnyOf?.Count ?? 0) == 0
                && (schema.OneOf?.Count ?? 0) == 0;
        }

        private static string TrimNullableTypeName(string typeName)
        {
            return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - 1) : typeName;
        }
    }
}
