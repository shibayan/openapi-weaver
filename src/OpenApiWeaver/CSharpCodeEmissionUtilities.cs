using System.Text;

namespace OpenApiWeaver;

internal static class CSharpCodeEmissionUtilities
{
    private static readonly HashSet<string> s_wellKnownHttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get", "Put", "Post", "Delete", "Head", "Options", "Trace", "Patch"
    };

    public static string EscapeStringLiteral(string value)
        => EscapeByLookup(value, static ch => ch switch
        {
            '\\' => "\\\\",
            '"' => "\\\"",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\0' => "\\0",
            _ => null
        });

    public static string GetHttpMethodExpression(string operationType)
    {
        var pascalCase = CSharpUtilities.ToPascalCase(operationType.ToLowerInvariant());
        return s_wellKnownHttpMethods.Contains(pascalCase)
            ? $"HttpMethod.{pascalCase}"
            : $"new HttpMethod(\"{EscapeStringLiteral(operationType.ToUpperInvariant())}\")";
    }

    public static void EmitDocComment(
        IndentedStringBuilder writer,
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
            AppendDocElement(writer, "summary", summary!);
        }

        if (hasRemarks)
        {
            AppendDocElement(writer, "remarks", remarks!);
        }

        if (documentedParameters is not null)
        {
            foreach (var parameter in documentedParameters)
            {
                AppendDocElement(writer, "param", parameter.Value!, $" name=\"{EscapeXmlDocumentationAttribute(parameter.Key)}\"");
            }
        }

        if (hasReturns)
        {
            AppendDocElement(writer, "returns", returns!);
        }
    }

    private static void AppendDocElement(IndentedStringBuilder writer, string elementName, string content, string? attributes = null)
    {
        var sanitizedContent = SanitizeDocumentationContent(content);

        writer.Append("/// <").Append(elementName);
        if (!string.IsNullOrEmpty(attributes))
        {
            writer.Append(attributes);
        }

        writer.AppendLine(">");
        foreach (var line in SplitDocumentationLines(sanitizedContent))
        {
            writer.Append("/// ").AppendLine(EscapeXmlDocumentationText(line));
        }

        writer.Append("/// </").Append(elementName).AppendLine(">");
    }

    private static IEnumerable<string> SplitDocumentationLines(string text)
    {
        return text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.Trim());
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
        => EscapeByLookup(value, static ch => ch switch
        {
            '&' => "&amp;",
            '<' => "&lt;",
            '>' => "&gt;",
            _ => null
        });

    private static string EscapeXmlDocumentationAttribute(string value)
        => EscapeByLookup(value, static ch => ch switch
        {
            '&' => "&amp;",
            '<' => "&lt;",
            '>' => "&gt;",
            '"' => "&quot;",
            '\'' => "&apos;",
            _ => null
        });

    private static string EscapeByLookup(string value, Func<char, string?> lookup)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            var replacement = lookup(ch);

            if (replacement is null)
            {
                builder?.Append(ch);
                continue;
            }

            if (builder is null)
            {
                builder = new StringBuilder(value.Length + 8);
                builder.Append(value, 0, i);
            }

            builder.Append(replacement);
        }

        return builder?.ToString() ?? value;
    }
}
