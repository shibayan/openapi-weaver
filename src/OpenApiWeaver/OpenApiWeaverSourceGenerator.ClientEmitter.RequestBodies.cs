using System.Text;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class ClientEmitter
    {
        private void EmitRequestBodyContentAssignment(StringBuilder builder, RequestBodyInfo requestBody, bool nullableBody)
        {
            var bodyValue = nullableBody ? "body!" : "body";

            switch (requestBody.Kind)
            {
                case RequestBodyKind.FormUrlEncoded:
                    EmitFormUrlEncodedContentAssignment(builder, requestBody, bodyValue);
                    break;
                case RequestBodyKind.MultipartFormData:
                    EmitMultipartFormDataContentAssignment(builder, requestBody, bodyValue);
                    break;
                default:
                    builder.Append("            request.Content = JsonContent.Create(").Append(bodyValue).AppendLine(", options: OpenApiClientHelpers.SerializerOptions);");
                    break;
            }
        }

        private static void EmitFormUrlEncodedContentAssignment(StringBuilder builder, RequestBodyInfo requestBody, string bodyValue)
        {
            builder.AppendLine("            var values = new List<KeyValuePair<string, string>>();");
            foreach (var property in requestBody.Properties)
            {
                var propertyAccess = $"{bodyValue}.{property.PropertyName}";
                EmitPropertyNullCheckStart(builder, propertyAccess, property.Nullable, indentLevel: 3);

                switch (property.Kind)
                {
                    case RequestBodyValueKind.Scalar:
                        EmitFormValueAdd(builder, property.SerializedName, propertyAccess, indentLevel: property.Nullable ? 4 : 3);
                        break;
                    case RequestBodyValueKind.Collection:
                        EmitCollectionLoopStart(builder, propertyAccess, indentLevel: property.Nullable ? 4 : 3);
                        EmitElementNullCheckStart(builder, property.ElementNullable, indentLevel: property.Nullable ? 5 : 4);
                        EmitFormValueAdd(builder, property.SerializedName, "item", indentLevel: property.Nullable ? (property.ElementNullable ? 6 : 5) : (property.ElementNullable ? 5 : 4));
                        EmitElementNullCheckEnd(builder, property.ElementNullable, indentLevel: property.Nullable ? 5 : 4);
                        EmitCollectionLoopEnd(builder, indentLevel: property.Nullable ? 4 : 3);
                        break;
                }

                EmitPropertyNullCheckEnd(builder, property.Nullable, indentLevel: 3);
            }

            builder.AppendLine("            request.Content = new FormUrlEncodedContent(values);");
        }

        private static void EmitMultipartFormDataContentAssignment(StringBuilder builder, RequestBodyInfo requestBody, string bodyValue)
        {
            builder.AppendLine("            var content = new MultipartFormDataContent();");
            foreach (var property in requestBody.Properties)
            {
                var propertyAccess = $"{bodyValue}.{property.PropertyName}";
                EmitPropertyNullCheckStart(builder, propertyAccess, property.Nullable, indentLevel: 3);

                switch (property.Kind)
                {
                    case RequestBodyValueKind.Scalar:
                    case RequestBodyValueKind.Binary:
                        EmitMultipartValueAdd(builder, property.SerializedName, propertyAccess, property.Kind, indentLevel: property.Nullable ? 4 : 3);
                        break;
                    case RequestBodyValueKind.Collection:
                        EmitCollectionLoopStart(builder, propertyAccess, indentLevel: property.Nullable ? 4 : 3);
                        EmitElementNullCheckStart(builder, property.ElementNullable, indentLevel: property.Nullable ? 5 : 4);
                        EmitMultipartValueAdd(builder, property.SerializedName, "item", property.ElementKind!.Value, indentLevel: property.Nullable ? (property.ElementNullable ? 6 : 5) : (property.ElementNullable ? 5 : 4));
                        EmitElementNullCheckEnd(builder, property.ElementNullable, indentLevel: property.Nullable ? 5 : 4);
                        EmitCollectionLoopEnd(builder, indentLevel: property.Nullable ? 4 : 3);
                        break;
                }

                EmitPropertyNullCheckEnd(builder, property.Nullable, indentLevel: 3);
            }

            builder.AppendLine("            request.Content = content;");
        }

        private static void EmitFormValueAdd(StringBuilder builder, string name, string valueExpression, int indentLevel)
        {
            builder.Append(' ', indentLevel * 4)
                .Append("values.Add(new KeyValuePair<string, string>(\"")
                .Append(EscapeStringLiteral(name))
                .Append("\", OpenApiClientHelpers.FormatParameter(")
                .Append(valueExpression)
                .AppendLine(")));");
        }

        private static void EmitMultipartValueAdd(StringBuilder builder, string name, string valueExpression, RequestBodyValueKind kind, int indentLevel)
        {
            builder.Append(' ', indentLevel * 4).Append("content.Add(");
            switch (kind)
            {
                case RequestBodyValueKind.Binary:
                    builder.Append("new ByteArrayContent(").Append(valueExpression).Append(")")
                        .Append(", \"").Append(EscapeStringLiteral(name)).Append("\", \"").Append(EscapeStringLiteral(name)).AppendLine("\");");
                    break;
                case RequestBodyValueKind.Scalar:
                    builder.Append("new StringContent(OpenApiClientHelpers.FormatParameter(").Append(valueExpression).Append("))")
                        .Append(", \"").Append(EscapeStringLiteral(name)).AppendLine("\");");
                    break;
                default:
                    throw new InvalidOperationException("Unsupported multipart value kind.");
            }
        }

        private static void EmitPropertyNullCheckStart(StringBuilder builder, string propertyAccess, bool nullable, int indentLevel)
        {
            if (!nullable)
            {
                return;
            }

            builder.Append(' ', indentLevel * 4).Append("if (").Append(propertyAccess).AppendLine(" is not null)");
            builder.Append(' ', indentLevel * 4).AppendLine("{");
        }

        private static void EmitPropertyNullCheckEnd(StringBuilder builder, bool nullable, int indentLevel)
        {
            if (!nullable)
            {
                return;
            }

            builder.Append(' ', indentLevel * 4).AppendLine("}");
        }

        private static void EmitCollectionLoopStart(StringBuilder builder, string propertyAccess, int indentLevel)
        {
            builder.Append(' ', indentLevel * 4).Append("foreach (var item in ").Append(propertyAccess).AppendLine(")");
            builder.Append(' ', indentLevel * 4).AppendLine("{");
        }

        private static void EmitCollectionLoopEnd(StringBuilder builder, int indentLevel)
        {
            builder.Append(' ', indentLevel * 4).AppendLine("}");
        }

        private static void EmitElementNullCheckStart(StringBuilder builder, bool nullable, int indentLevel)
        {
            if (!nullable)
            {
                return;
            }

            builder.Append(' ', indentLevel * 4).AppendLine("if (item is not null)");
            builder.Append(' ', indentLevel * 4).AppendLine("{");
        }

        private static void EmitElementNullCheckEnd(StringBuilder builder, bool nullable, int indentLevel)
        {
            if (!nullable)
            {
                return;
            }

            builder.Append(' ', indentLevel * 4).AppendLine("}");
        }
    }
}
