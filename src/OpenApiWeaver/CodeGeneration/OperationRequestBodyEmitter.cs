using static OpenApiWeaver.CodeGeneration.CSharpCodeEmissionUtilities;

namespace OpenApiWeaver.CodeGeneration;

internal sealed class OperationRequestBodyEmitter(ClientModel model)
{
    public void EmitContentAssignment(IndentedStringBuilder writer, RequestBodyInfo requestBody, bool nullableBody)
    {
        var bodyValue = nullableBody ? requestBody.ParameterName + "!" : requestBody.ParameterName;

        switch (requestBody.Kind)
        {
            case RequestBodyKind.FormUrlEncoded:
                EmitFormUrlEncodedContentAssignment(writer, requestBody, bodyValue);
                break;
            case RequestBodyKind.MultipartFormData:
                EmitMultipartFormDataContentAssignment(writer, requestBody, bodyValue);
                break;
            default:
                writer.Append("request.Content = JsonContent.Create(")
                    .Append(bodyValue)
                    .Append(", mediaType: System.Net.Http.Headers.MediaTypeHeaderValue.Parse(\"")
                    .Append(EscapeStringLiteral(requestBody.ContentType))
                    .Append("\"), options: ")
                    .Append(JsonSerializerOptionsEmitter.GetOptionsExpression(model, JsonSerializerDirection.Request))
                    .AppendLine(");");
                break;
        }
    }

    private static void EmitFormUrlEncodedContentAssignment(IndentedStringBuilder writer, RequestBodyInfo requestBody, string bodyValue)
    {
        writer.AppendLine("var values = new List<KeyValuePair<string, string>>();");
        foreach (var property in requestBody.Properties)
        {
            var propertyAccess = $"{bodyValue}.{property.PropertyName}";
            EmitIfNotNull(writer, propertyAccess, property.IsNullable, () =>
            {
                switch (property.Kind)
                {
                    case RequestBodyValueKind.Scalar:
                        EmitFormValueAdd(writer, property.WireName, propertyAccess);
                        break;
                    case RequestBodyValueKind.Collection:
                        EmitCollectionLoop(writer, propertyAccess, () => EmitIfNotNull(writer, "item", property.IsElementNullable, () => EmitFormValueAdd(writer, property.WireName, "item")));
                        break;
                }
            });
        }

        writer.AppendLine("request.Content = new FormUrlEncodedContent(values);");
    }

    private static void EmitMultipartFormDataContentAssignment(IndentedStringBuilder writer, RequestBodyInfo requestBody, string bodyValue)
    {
        writer.AppendLine("var content = new MultipartFormDataContent();");
        foreach (var property in requestBody.Properties)
        {
            var propertyAccess = $"{bodyValue}.{property.PropertyName}";
            EmitIfNotNull(writer, propertyAccess, property.IsNullable, () =>
            {
                switch (property.Kind)
                {
                    case RequestBodyValueKind.Scalar:
                    case RequestBodyValueKind.Binary:
                        EmitMultipartValueAdd(writer, property.WireName, propertyAccess, property.Kind);
                        break;
                    case RequestBodyValueKind.Collection:
                        EmitCollectionLoop(writer, propertyAccess, () => EmitIfNotNull(writer, "item", property.IsElementNullable, () => EmitMultipartValueAdd(writer, property.WireName, "item", property.ElementKind!.Value)));
                        break;
                }
            });
        }

        writer.AppendLine("request.Content = content;");
    }

    private static void EmitFormValueAdd(IndentedStringBuilder writer, string name, string valueExpression)
    {
        writer
            .Append("values.Add(new KeyValuePair<string, string>(\"")
            .Append(EscapeStringLiteral(name))
            .Append("\", OpenApiClientHelpers.FormatParameter(")
            .Append(valueExpression)
            .AppendLine(")));");
    }

    private static void EmitMultipartValueAdd(IndentedStringBuilder writer, string name, string valueExpression, RequestBodyValueKind kind)
    {
        writer.Append("content.Add(");
        switch (kind)
        {
            case RequestBodyValueKind.Binary:
                writer.Append("new ByteArrayContent(").Append(valueExpression).Append(")")
                    .Append(", \"").Append(EscapeStringLiteral(name)).Append("\", \"").Append(EscapeStringLiteral(name)).AppendLine("\");");
                break;
            case RequestBodyValueKind.Scalar:
                writer.Append("new StringContent(OpenApiClientHelpers.FormatParameter(").Append(valueExpression).Append("))")
                    .Append(", \"").Append(EscapeStringLiteral(name)).AppendLine("\");");
                break;
            default:
                throw new InvalidOperationException("Unsupported multipart value kind.");
        }
    }

    private static void EmitIfNotNull(IndentedStringBuilder writer, string expression, bool nullable, Action emitBody)
    {
        if (!nullable)
        {
            emitBody();
            return;
        }

        writer.Append("if (").Append(expression).AppendLine(" is not null)");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            emitBody();
        }

        writer.AppendLine("}");
    }

    private static void EmitCollectionLoop(IndentedStringBuilder writer, string expression, Action emitBody)
    {
        writer.Append("foreach (var item in ").Append(expression).AppendLine(")");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            emitBody();
        }

        writer.AppendLine("}");
    }
}
