using System.Text;

namespace OpenApiWeaver.CodeGeneration;

internal static class CSharpUtilities
{
    private static readonly HashSet<string> s_reservedIdentifiers = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };

    public static string SafeIdentifier(string value)
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

    public static string ToPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (!char.IsLetterOrDigit(ch))
            {
                if (normalized.Length > 0 && normalized[normalized.Length - 1] != ' ')
                {
                    normalized.Append(' ');
                }

                continue;
            }

            if (normalized.Length > 0 && normalized[normalized.Length - 1] != ' ')
            {
                if (char.IsUpper(ch) && !char.IsUpper(normalized[normalized.Length - 1]))
                {
                    // Boundary: lowercase/digit → uppercase (e.g., "listH" → "list H")
                    normalized.Append(' ');
                }
                else if (char.IsUpper(ch) && i + 1 < value.Length && char.IsLower(value[i + 1])
                         && normalized.Length >= 2 && char.IsUpper(normalized[normalized.Length - 1]))
                {
                    // Boundary: acronym end (e.g., "PR" in "HTTPResponse" → "HTTP Response")
                    normalized.Append(' ');
                }
            }

            normalized.Append(ch);
        }

        var result = new StringBuilder(normalized.Length);
        var atWordStart = true;
        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            if (ch == ' ')
            {
                atWordStart = true;
                continue;
            }

            result.Append(atWordStart ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
            atWordStart = false;
        }

        return result.ToString();
    }

    public static string ToCamelCase(string value)
    {
        var pascal = ToPascalCase(value);
        return string.IsNullOrEmpty(pascal) ? "value" : char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
    }

    private static bool IsIdentifierStartCharacter(char ch)
    {
        return ch == '_' || char.IsLetter(ch);
    }

    public static string MakeNullableTypeName(string typeName)
    {
        return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName : $"{typeName}?";
    }

    public static string TrimNullableTypeName(string typeName)
    {
        return typeName.EndsWith("?", StringComparison.Ordinal) ? typeName.Substring(0, typeName.Length - 1) : typeName;
    }
}
