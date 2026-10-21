namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private static partial class SupportTypesEmitter
    {
        private static void EmitHelperClass(IndentedStringBuilder writer)
        {
            writer.Append("internal static class ").Append(SupportTypeNames.ClientHelpers).AppendLine();
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.Append("internal static readonly JsonSerializerOptions ").Append(SupportTypeNames.SerializerOptions).AppendLine(" = CreateSerializerOptions();");
                writer.AppendLine();
                writer.AppendLine("internal static JsonSerializerOptions CreateSerializerOptions()");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);");
                    EmitAllowOutOfOrderMetadataProperties(writer, "options");
                    writer.AppendLine("return options;");
                }

                writer.AppendLine("}");
                writer.AppendLine();

                EmitParameterFormattingHelpers(writer);
                writer.AppendLine();
                EmitQueryHelpers(writer);
                writer.AppendLine();
                EmitCookieHelpers(writer);
                writer.AppendLine();
                EmitResponseHelpers(writer);
            }

            writer.AppendLine("}");
        }

        private static void EmitAllowOutOfOrderMetadataProperties(IndentedStringBuilder writer, string optionsExpression)
        {
            // Servers are not required to write the discriminator first; the option only exists in System.Text.Json 9+.
            writer.AppendLine("#if NET9_0_OR_GREATER");
            writer.Append(optionsExpression).AppendLine(".AllowOutOfOrderMetadataProperties = true;");
            writer.AppendLine("#endif");
        }
    }
}
