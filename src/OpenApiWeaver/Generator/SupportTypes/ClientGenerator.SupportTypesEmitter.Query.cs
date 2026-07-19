namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private static partial class SupportTypesEmitter
    {
        // Overloads emitted per element type so generated call sites bind to a statically
        // typed FormatParameter instead of the boxing object-based fallback. The final
        // non-generic IEnumerable overload catches element types the others cannot accept.
        private static readonly (string TypeParameters, string ValuesType, string Constraint)[] s_collectionOverloads =
        [
            (string.Empty, "IEnumerable<string?>", string.Empty),
            (string.Empty, "IEnumerable<bool>", string.Empty),
            ("<T>", "IEnumerable<T>", " where T : struct, IFormattable"),
            (string.Empty, "System.Collections.IEnumerable", string.Empty)
        ];

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

            foreach (var (typeParameters, valuesType, constraint) in s_collectionOverloads)
            {
                writer.AppendLine();
                writer.Append("internal static void AppendQueryParameters").Append(typeParameters).Append("(StringBuilder builder, ref bool hasQuery, string name, ").Append(valuesType).Append(" values)").AppendLine(constraint);
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
            }

            foreach (var (typeParameters, valuesType, constraint) in s_collectionOverloads)
            {
                writer.AppendLine();
                writer.Append("internal static string FormatCollectionParameter").Append(typeParameters).Append('(').Append(valuesType).Append("? values)").AppendLine(constraint);
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
}
