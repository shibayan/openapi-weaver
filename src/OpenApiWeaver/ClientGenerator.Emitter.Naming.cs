using System.Text;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Emitter
    {
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
