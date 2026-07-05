namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private static partial class SupportTypesEmitter
    {
        private static void EmitQueryHelpers(IndentedStringBuilder writer)
        {
            writer.AppendLine("internal static void AppendQueryParameter(StringBuilder builder, ref bool hasQuery, string name, string value)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("builder.Append(hasQuery ? '&' : '?');");
                writer.AppendLine("hasQuery = true;");
                writer.AppendLine("builder.Append(name);");
                writer.AppendLine("builder.Append('=');");
                writer.AppendLine("builder.Append(Uri.EscapeDataString(value));");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("internal static void AppendQueryParameters<T>(StringBuilder builder, ref bool hasQuery, string name, IEnumerable<T> values)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("foreach (var value in values)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("AppendQueryParameter(builder, ref hasQuery, name, FormatParameter(value));");
                }

                writer.AppendLine("}");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("internal static string FormatCollectionParameter<T>(IEnumerable<T>? values)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("if (values is null)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("return string.Empty;");
                }

                writer.AppendLine("}");
                writer.AppendLine();
                writer.AppendLine("var builder = new StringBuilder();");
                writer.AppendLine("foreach (var value in values)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("if (builder.Length > 0)");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        writer.AppendLine("builder.Append(',');");
                    }

                    writer.AppendLine("}");
                    writer.AppendLine();
                    writer.AppendLine("builder.Append(FormatParameter(value));");
                }

                writer.AppendLine("}");
                writer.AppendLine();
                writer.AppendLine("return builder.ToString();");
            }

            writer.AppendLine("}");
        }
    }
}
