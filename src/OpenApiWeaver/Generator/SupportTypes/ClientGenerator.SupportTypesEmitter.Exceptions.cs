namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private static partial class SupportTypesEmitter
    {
        private static void EmitExceptionTypes(IndentedStringBuilder writer)
        {
            writer.Append("public class ").Append(SupportTypeNames.Exception).AppendLine(" : Exception");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.Append("public ").Append(SupportTypeNames.Exception).AppendLine("(int statusCode, string? reasonPhrase, string? contentType, string? responseContent, Exception? innerException = null)");
                writer.AppendLine(": base(CreateMessage(statusCode, reasonPhrase), innerException)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("StatusCode = statusCode;");
                    writer.AppendLine("ReasonPhrase = reasonPhrase;");
                    writer.AppendLine("ContentType = contentType;");
                    writer.AppendLine("ResponseContent = responseContent;");
                }

                writer.AppendLine("}");
                writer.AppendLine();
                writer.AppendLine("public int StatusCode { get; }");
                writer.AppendLine();
                writer.AppendLine("public string? ReasonPhrase { get; }");
                writer.AppendLine();
                writer.AppendLine("public string? ContentType { get; }");
                writer.AppendLine();
                writer.AppendLine("public string? ResponseContent { get; }");
                writer.AppendLine();
                writer.AppendLine("private static string CreateMessage(int statusCode, string? reasonPhrase)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("return string.IsNullOrWhiteSpace(reasonPhrase)");
                    using (writer.PushIndent())
                    {
                        writer.AppendLine("? $\"The HTTP request failed with status code {statusCode}.\"");
                        writer.AppendLine(": $\"The HTTP request failed with status code {statusCode} ({reasonPhrase}).\";");
                    }
                }

                writer.AppendLine("}");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.Append("public class ").Append(SupportTypeNames.Exception).Append("<TError> : ").Append(SupportTypeNames.Exception).AppendLine();
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.Append("public ").Append(SupportTypeNames.Exception).AppendLine("(int statusCode, string? reasonPhrase, string? contentType, string? responseContent, TError? error, Exception? innerException = null)");
                writer.AppendLine(": base(statusCode, reasonPhrase, contentType, responseContent, innerException)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("Error = error;");
                }

                writer.AppendLine("}");
                writer.AppendLine();
                writer.AppendLine("public TError? Error { get; }");
            }

            writer.AppendLine("}");
        }
    }
}
