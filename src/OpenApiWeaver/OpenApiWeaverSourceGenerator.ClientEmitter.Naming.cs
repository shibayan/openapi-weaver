using System.Text;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class ClientEmitter
    {
        private static bool IsReferenceLikeType(string typeName)
        {
            return typeName is "string"
                || typeName.StartsWith("IReadOnlyList<", StringComparison.Ordinal)
                || (!typeName.EndsWith("?", StringComparison.Ordinal) && typeName is not "int" and not "long" and not "float" and not "double" and not "decimal" and not "bool" and not "DateOnly" and not "DateTimeOffset" and not "Guid" and not "JsonElement");
        }

        private bool RequiresNonNullJsonResponse(string typeName)
        {
            return !typeName.EndsWith("?", StringComparison.Ordinal)
                && !IsGeneratedEnumType(typeName)
                && IsReferenceLikeType(typeName);
        }

        private bool IsGeneratedEnumType(string typeName)
        {
            var trimmedTypeName = TrimNullableTypeName(typeName);
            foreach (var schema in model.Schemas)
            {
                if (schema.IsEnum && string.Equals(schema.TypeName, trimmedTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string EscapeStringLiteral(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                .Replace("\0", "\\0");
        }

        private static void EmitSecuritySchemeInitialization(IndentedStringBuilder writer, SecuritySchemeBinding securityScheme)
        {
            if (securityScheme.Location == SecuritySchemeLocation.Query)
            {
                writer.Append(securityScheme.FieldName).Append(" = ").Append(securityScheme.ParameterName).AppendLine(";");
                return;
            }

            writer.Append("if (").Append(securityScheme.ParameterName).AppendLine(" is not null)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                if (securityScheme.IsBearerToken)
                {
                    writer.Append("_httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", ").Append(securityScheme.ParameterName).AppendLine(");");
                }
                else if (securityScheme.Location == SecuritySchemeLocation.Cookie)
                {
                    writer.Append("_httpClient.DefaultRequestHeaders.TryAddWithoutValidation(\"Cookie\", \"").Append(securityScheme.HeaderOrParameterName).Append("=\" + ").Append(securityScheme.ParameterName).AppendLine(");");
                }
                else
                {
                    writer.Append("_httpClient.DefaultRequestHeaders.TryAddWithoutValidation(\"").Append(securityScheme.HeaderOrParameterName).Append("\", ").Append(securityScheme.ParameterName).AppendLine(");");
                }
            }

            writer.AppendLine("}");
        }

        private static readonly HashSet<string> s_wellKnownHttpMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            "Get", "Put", "Post", "Delete", "Head", "Options", "Trace", "Patch"
        };

        private static string GetHttpMethodExpression(string operationType)
        {
            var pascalCase = ToPascalCase(operationType.ToLowerInvariant());
            return s_wellKnownHttpMethods.Contains(pascalCase)
                ? $"HttpMethod.{pascalCase}"
                : $"new HttpMethod(\"{EscapeStringLiteral(operationType.ToUpperInvariant())}\")";
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

        private static void EmitDocComment(
            IndentedStringBuilder writer,
            string? summary = null,
            string? remarks = null,
            IEnumerable<KeyValuePair<string, string?>>? parameters = null,
            string? returns = null)
        {
            EmitDocComment(writer.Builder, writer.CurrentIndent, summary, remarks, parameters, returns);
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
