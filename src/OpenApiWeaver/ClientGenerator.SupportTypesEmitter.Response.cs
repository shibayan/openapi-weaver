namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private static partial class SupportTypesEmitter
    {
        private static void EmitResponseHelpers(IndentedStringBuilder writer)
        {
            writer.AppendLine("internal static bool HasJsonContentType(string? contentType)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("return !string.IsNullOrWhiteSpace(contentType) && contentType.Contains(\"json\", StringComparison.OrdinalIgnoreCase);");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("internal static bool ResponseMatchesStatusCode(int statusCode, string statusCodePattern)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("if (string.Equals(statusCodePattern, \"default\", StringComparison.OrdinalIgnoreCase))");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("return true;");
                }

                writer.AppendLine("}");
                writer.AppendLine();
                writer.AppendLine("if (statusCodePattern.Length == 3");
                using (writer.PushIndent())
                {
                    writer.AppendLine("&& char.IsDigit(statusCodePattern[0])");
                    writer.AppendLine("&& statusCodePattern[1] is 'X' or 'x'");
                    writer.AppendLine("&& statusCodePattern[2] is 'X' or 'x')");
                }

                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("var prefix = statusCodePattern[0] - '0';");
                    writer.AppendLine("return statusCode >= prefix * 100 && statusCode < (prefix + 1) * 100;");
                }

                writer.AppendLine("}");
                writer.AppendLine();
                writer.AppendLine("return int.TryParse(statusCodePattern, out var expectedStatusCode) && statusCode == expectedStatusCode;");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("internal static T? DeserializeResponseContent<T>(string? responseContent)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.Append("return DeserializeResponseContent<T>(responseContent, ").Append(SupportTypeNames.SerializerOptions).AppendLine(");");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("internal static T? DeserializeResponseContent<T>(string? responseContent, JsonSerializerOptions options)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("return string.IsNullOrWhiteSpace(responseContent)");
                using (writer.PushIndent())
                {
                    writer.AppendLine("? default");
                    writer.AppendLine(": JsonSerializer.Deserialize<T>(responseContent, options);");
                }
            }

            writer.AppendLine("}");
        }
    }
}
