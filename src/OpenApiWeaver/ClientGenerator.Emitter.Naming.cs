using System.Text;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Emitter
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
            return _generatedEnumTypeNames.Contains(TrimNullableTypeName(typeName));
        }

        private static string EscapeStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            StringBuilder? builder = null;
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                var replacement = ch switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\0' => "\\0",
                    _ => null
                };

                if (replacement is null)
                {
                    builder?.Append(ch);
                    continue;
                }

                if (builder is null)
                {
                    builder = new StringBuilder(value.Length + 4);
                    builder.Append(value, 0, i);
                }

                builder.Append(replacement);
            }

            return builder?.ToString() ?? value;
        }

        private static void EmitSecuritySchemeInitialization(IndentedStringBuilder writer, SecuritySchemeBinding securityScheme)
        {
            writer.Append(securityScheme.FieldName).Append(" = ").Append(securityScheme.ParameterName).AppendLine(";");
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

    }
}
