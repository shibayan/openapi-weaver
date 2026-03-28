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
            var indices = new Dictionary<(ParameterLocation Location, string Name), int>();
            if (pathParameters is not null)
            {
                foreach (var parameter in pathParameters)
                {
                    AddOrReplaceParameter(parameters, indices, parameter);
                }
            }

            if (operationParameters is not null)
            {
                foreach (var parameter in operationParameters)
                {
                    AddOrReplaceParameter(parameters, indices, parameter);
                }
            }

            return parameters;
        }

        private static void AddOrReplaceParameter(
            List<IOpenApiParameter> parameters,
            Dictionary<(ParameterLocation Location, string Name), int> indices,
            IOpenApiParameter parameter)
        {
            var location = parameter.In ?? ParameterLocation.Query;
            var key = (
                location,
                location == ParameterLocation.Header
                    ? (parameter.Name ?? string.Empty).ToUpperInvariant()
                    : parameter.Name ?? string.Empty);
            if (indices.TryGetValue(key, out var index))
            {
                parameters[index] = parameter;
                return;
            }

            indices.Add(key, parameters.Count);
            parameters.Add(parameter);
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

            if (string.Equals(operationType, "get", StringComparison.OrdinalIgnoreCase)
                && TryBuildCanonicalGetMethodName(route, tagName, filteredTokens) is { } canonicalGetName)
            {
                return canonicalGetName;
            }

            return SafeIdentifier(string.Concat(filteredTokens.Select(static token => ToPascalCase(token ?? string.Empty))));
        }

        private static string? TryBuildCanonicalGetMethodName(string route, string? tagName, IReadOnlyList<string> filteredTokens)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            var normalizedTag = NormalizeToken(tagName!);
            if (normalizedTag.Length == 0 || !IsSelfReferentialGetName(filteredTokens, normalizedTag))
            {
                return null;
            }

            var segments = route.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            var lastSegment = segments[segments.Length - 1];
            if (!IsPathParameterSegment(lastSegment)
                && NormalizeToken(lastSegment) == normalizedTag)
            {
                return "List";
            }

            if (segments.Length >= 2
                && IsPathParameterSegment(lastSegment)
                && NormalizeToken(segments[segments.Length - 2]) == normalizedTag)
            {
                return "Get";
            }

            return null;
        }

        private static bool IsSelfReferentialGetName(IReadOnlyList<string> filteredTokens, string normalizedTag)
        {
            if (filteredTokens.Count == 0)
            {
                return true;
            }

            var index = 0;
            if (IsCanonicalGetVerb(filteredTokens[0]))
            {
                index++;
            }

            if (index >= filteredTokens.Count)
            {
                return true;
            }

            return filteredTokens.Skip(index).All(token => token is not null && NormalizeToken(token) == normalizedTag);
        }

        private static bool IsCanonicalGetVerb(string token)
        {
            var normalized = NormalizeToken(token);
            return normalized is "get" or "list";
        }

        private static bool IsPathParameterSegment(string segment)
        {
            return segment.Length > 2
                && segment[0] == '{'
                && segment[segment.Length - 1] == '}';
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
            if (normalized.Length > 3
                && normalized.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(0, normalized.Length - 3) + "y";
            }

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

        private static void EmitDocComment(
            StringBuilder builder,
            string indent,
            string? summary = null,
            string? remarks = null,
            IEnumerable<KeyValuePair<string, string?>>? parameters = null,
            string? returns = null)
        {
            var hasSummary = !string.IsNullOrWhiteSpace(summary);
            var hasRemarks = !string.IsNullOrWhiteSpace(remarks);
            var documentedParameters = parameters?
                .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
                .ToList();
            var hasReturns = !string.IsNullOrWhiteSpace(returns);

            if (!hasSummary && !hasRemarks && (documentedParameters is null || documentedParameters.Count == 0) && !hasReturns)
            {
                return;
            }

            if (hasSummary)
            {
                AppendDocElement(builder, indent, "summary", summary!);
            }

            if (hasRemarks)
            {
                AppendDocElement(builder, indent, "remarks", remarks!);
            }

            if (documentedParameters is not null)
            {
                foreach (var parameter in documentedParameters)
                {
                    AppendDocElement(builder, indent, "param", parameter.Value!, $" name=\"{EscapeXmlDocumentationAttribute(parameter.Key)}\"");
                }
            }

            if (hasReturns)
            {
                AppendDocElement(builder, indent, "returns", returns!);
            }
        }

        private static void AppendDocElement(StringBuilder builder, string indent, string elementName, string content, string? attributes = null)
        {
            var sanitizedContent = SanitizeDocumentationContent(content);

            builder.Append(indent).Append("/// <").Append(elementName);
            if (!string.IsNullOrEmpty(attributes))
            {
                builder.Append(attributes);
            }

            builder.AppendLine(">");
            foreach (var line in SplitDocumentationLines(sanitizedContent))
            {
                builder.Append(indent).Append("/// ").AppendLine(EscapeXmlDocumentationText(line));
            }

            builder.Append(indent).Append("/// </").Append(elementName).AppendLine(">");
        }

        private static IEnumerable<string> SplitDocumentationLines(string text)
        {
            return text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(static line => line.Trim())
                .DefaultIfEmpty(string.Empty);
        }

        private static string SanitizeDocumentationContent(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            var insideTag = false;
            var tagBuilder = new StringBuilder();

            foreach (var ch in value)
            {
                if (insideTag)
                {
                    if (ch == '>')
                    {
                        AppendTagSeparator(builder, tagBuilder.ToString());
                        tagBuilder.Clear();
                        insideTag = false;
                    }
                    else
                    {
                        tagBuilder.Append(ch);
                    }

                    continue;
                }

                if (ch == '<')
                {
                    insideTag = true;
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static void AppendTagSeparator(StringBuilder builder, string tagContent)
        {
            var tagName = GetHtmlTagName(tagContent);
            if (tagName.Length == 0)
            {
                return;
            }

            if (tagName is "br" or "p" or "/p" or "div" or "/div" or "li" or "/li" or "ul" or "/ul" or "ol" or "/ol")
            {
                if (builder.Length == 0 || builder[builder.Length - 1] == '\n')
                {
                    return;
                }

                builder.AppendLine();
            }
        }

        private static string GetHtmlTagName(string tagContent)
        {
            if (string.IsNullOrWhiteSpace(tagContent))
            {
                return string.Empty;
            }

            var trimmed = tagContent.Trim();
            var endIndex = 0;
            while (endIndex < trimmed.Length
                && !char.IsWhiteSpace(trimmed[endIndex])
                && trimmed[endIndex] != '/')
            {
                endIndex++;
            }

            if (trimmed[0] == '/')
            {
                var closingEndIndex = 1;
                while (closingEndIndex < trimmed.Length
                    && !char.IsWhiteSpace(trimmed[closingEndIndex])
                    && trimmed[closingEndIndex] != '/')
                {
                    closingEndIndex++;
                }

                return "/" + trimmed.Substring(1, closingEndIndex - 1).ToLowerInvariant();
            }

            return trimmed.Substring(0, endIndex).ToLowerInvariant();
        }

        private static string EscapeXmlDocumentationText(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private static string EscapeXmlDocumentationAttribute(string value)
        {
            return EscapeXmlDocumentationText(value)
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
