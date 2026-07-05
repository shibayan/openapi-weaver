using static OpenApiWeaver.CodeGeneration.CSharpCodeEmissionUtilities;

namespace OpenApiWeaver.CodeGeneration;

internal sealed partial class OperationEmitter
{
    private void EmitErrorResponseHandling(IndentedStringBuilder writer, OperationGroupItem operation)
    {
        writer.AppendLine("var statusCode = (int)response.StatusCode;");
        writer.AppendLine("var contentType = response.Content?.Headers?.ContentType?.MediaType;");
        writer.AppendLine("var responseContent = response.Content is null");
        using (writer.PushIndent())
        {
            writer.AppendLine("? null");
            writer.AppendLine(": await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);");
        }

        foreach (var errorResponse in operation.ErrorResponses)
        {
            writer.Append("if (OpenApiClientHelpers.ResponseMatchesStatusCode(statusCode, \"").Append(EscapeStringLiteral(errorResponse.StatusCodePattern)).AppendLine("\"))");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                EmitTypedErrorResponseHandling(writer, errorResponse.Response);
            }

            writer.AppendLine("}");
        }

        writer.AppendLine("throw new OpenApiException(statusCode, response.ReasonPhrase, contentType, responseContent);");
    }

    private void EmitTypedErrorResponseHandling(IndentedStringBuilder writer, ResponseInfo response)
    {
        var errorTypeName = response.Type?.NonNullableCSharpTypeName ?? string.Empty;
        switch (response.Kind)
        {
            case ResponseKind.Json:
                writer.AppendLine("if (string.IsNullOrWhiteSpace(contentType) || OpenApiClientHelpers.HasJsonContentType(contentType))");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("try");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        var serializerOptions = GetSerializerOptionsExpression(JsonSerializerDirection.Response);
                        writer.Append("var error = OpenApiClientHelpers.DeserializeResponseContent<").Append(errorTypeName).Append(">(responseContent");
                        if (!string.Equals(serializerOptions, SupportTypeNames.DefaultSerializerOptionsExpression, StringComparison.Ordinal))
                        {
                            writer.Append(", ").Append(serializerOptions);
                        }

                        writer.AppendLine(");");
                        writer.Append("throw new OpenApiException<").Append(errorTypeName).AppendLine(">(statusCode, response.ReasonPhrase, contentType, responseContent, error);");
                    }

                    writer.AppendLine("}");
                    writer.AppendLine("catch (JsonException exception)");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        writer.AppendLine("throw new OpenApiException(statusCode, response.ReasonPhrase, contentType, responseContent, exception);");
                    }

                    writer.AppendLine("}");
                }

                writer.AppendLine("}");
                return;
            case ResponseKind.String:
                writer.Append("throw new OpenApiException<").Append(errorTypeName).AppendLine(">(statusCode, response.ReasonPhrase, contentType, responseContent, responseContent);");
                return;
            case ResponseKind.Binary:
            case ResponseKind.None:
                // No typed deserialization: the untyped OpenApiException emitted after the
                // dispatch loop handles these response shapes.
                return;
        }
    }
}
