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
                writer.Append("internal static readonly JsonSerializerOptions ").Append(SupportTypeNames.SerializerOptions).AppendLine(" = new(JsonSerializerDefaults.Web);");
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
    }
}
