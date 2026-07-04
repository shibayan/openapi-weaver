namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private static partial class SupportTypesEmitter
    {
        private static void EmitCookieHelpers(IndentedStringBuilder writer)
        {
            writer.AppendLine("internal static void AppendCookieParameter(StringBuilder builder, string name, string value)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("if (builder.Length > 0)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("builder.Append(\"; \");");
                }

                writer.AppendLine("}");
                writer.AppendLine();
                writer.AppendLine("builder.Append(name);");
                writer.AppendLine("builder.Append('=');");
                writer.AppendLine("builder.Append(Uri.EscapeDataString(value));");
            }

            writer.AppendLine("}");
        }
    }
}
