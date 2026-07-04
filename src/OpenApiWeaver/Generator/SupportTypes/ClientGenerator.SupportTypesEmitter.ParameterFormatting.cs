namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private static partial class SupportTypesEmitter
    {
        private static void EmitParameterFormattingHelpers(IndentedStringBuilder writer)
        {
            writer.AppendLine("internal static string FormatParameter(string? value) => value ?? string.Empty;");
            writer.AppendLine();
            writer.AppendLine("internal static string FormatParameter(JsonElement value) => value.ToString();");
            writer.AppendLine();
            writer.AppendLine("internal static string FormatParameter(bool value) => value ? \"true\" : \"false\";");
            writer.AppendLine();
            writer.AppendLine("internal static string FormatParameter(bool? value) => value is bool actual ? (actual ? \"true\" : \"false\") : string.Empty;");
            writer.AppendLine();
            writer.AppendLine("internal static string FormatParameter<T>(T value) where T : struct, IFormattable");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("return FormatFormattable(value);");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("internal static string FormatParameter<T>(T? value) where T : struct, IFormattable");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("return value is T actual ? FormatFormattable(actual) : string.Empty;");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("internal static string FormatParameter(object? value)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("return value switch");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("null => string.Empty,");
                    writer.AppendLine("bool boolean => boolean ? \"true\" : \"false\",");
                    writer.AppendLine("DateTimeOffset dateTimeOffset => dateTimeOffset.ToString(\"o\", CultureInfo.InvariantCulture),");
                    writer.AppendLine("DateTime dateTime => dateTime.ToString(\"o\", CultureInfo.InvariantCulture),");
                    writer.AppendLine("DateOnly dateOnly => dateOnly.ToString(\"yyyy-MM-dd\", CultureInfo.InvariantCulture),");
                    writer.AppendLine("TimeOnly timeOnly => timeOnly.ToString(\"HH:mm:ss.FFFFFFF\", CultureInfo.InvariantCulture),");
                    writer.AppendLine("IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),");
                    writer.AppendLine("_ => value.ToString() ?? string.Empty");
                }

                writer.AppendLine("};");
            }

            writer.AppendLine("}");
            writer.AppendLine();
            writer.AppendLine("private static string FormatFormattable<T>(T value) where T : struct, IFormattable");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.AppendLine("return value switch");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("DateTimeOffset dateTimeOffset => dateTimeOffset.ToString(\"o\", CultureInfo.InvariantCulture),");
                    writer.AppendLine("DateTime dateTime => dateTime.ToString(\"o\", CultureInfo.InvariantCulture),");
                    writer.AppendLine("DateOnly dateOnly => dateOnly.ToString(\"yyyy-MM-dd\", CultureInfo.InvariantCulture),");
                    writer.AppendLine("TimeOnly timeOnly => timeOnly.ToString(\"HH:mm:ss.FFFFFFF\", CultureInfo.InvariantCulture),");
                    writer.AppendLine("_ => value.ToString(null, CultureInfo.InvariantCulture)");
                }

                writer.AppendLine("};");
            }

            writer.AppendLine("}");
        }
    }
}
