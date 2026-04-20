namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Emitter
    {
        private static string EscapeStringLiteral(string value)
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
