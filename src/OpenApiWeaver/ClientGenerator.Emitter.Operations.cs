namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Emitter
    {
        private void EmitOperation(IndentedStringBuilder writer, OperationGroupItem operation)
        {
            var route = NormalizeRelativeRoute(operation.Route);
            var parameterDocumentation = new List<KeyValuePair<string, string?>>();
            var pathParameters = operation.Parameters.Where(static parameter => parameter.Location == ParameterLocation.Path).ToList();
            var queryParameters = operation.Parameters.Where(static parameter => parameter.Location == ParameterLocation.Query).ToList();
            var headerParameters = operation.Parameters.Where(static parameter => parameter.Location == ParameterLocation.Header).ToList();

            var requiredMethodParameters = new List<string>();
            var optionalMethodParameters = new List<string>();

            foreach (var parameter in operation.Parameters)
            {
                var parameterDeclaration = $"{parameter.TypeName} {parameter.ParameterName}";
                if (parameter.Required)
                {
                    requiredMethodParameters.Add(parameterDeclaration);
                }
                else
                {
                    optionalMethodParameters.Add($"{parameterDeclaration} = default");
                }

                parameterDocumentation.Add(new KeyValuePair<string, string?>(parameter.ParameterName, parameter.Description));
            }

            if (operation.RequestBody is not null)
            {
                var bodyTypeName = operation.RequestBody.IsRequired ? operation.RequestBody.TypeName : MakeNullableTypeName(operation.RequestBody.TypeName);
                var bodyParameter = $"{bodyTypeName} body";
                if (operation.RequestBody.IsRequired)
                {
                    requiredMethodParameters.Add(bodyParameter);
                }
                else
                {
                    optionalMethodParameters.Add($"{bodyParameter} = default");
                }

                parameterDocumentation.Add(new KeyValuePair<string, string?>("body", operation.RequestBody.Description));
            }

            var methodParameters = requiredMethodParameters
                .Concat(optionalMethodParameters)
                .Append("CancellationToken cancellationToken = default")
                .ToList();
            parameterDocumentation.Add(new KeyValuePair<string, string?>("cancellationToken", "A cancellation token that can be used to cancel the operation."));

            EmitDocComment(
                writer,
                summary: operation.Summary,
                remarks: operation.Remarks,
                parameters: parameterDocumentation,
                returns: operation.Response.Kind == ResponseKind.None ? null : operation.Response.Documentation);

            if (operation.Response.Kind == ResponseKind.None)
            {
                writer.Append("public async Task ").Append(operation.MethodName).Append("Async(").Append(string.Join(", ", methodParameters)).AppendLine(")");
            }
            else
            {
                writer.Append("public async Task<").Append(operation.Response.TypeName).Append("> ").Append(operation.MethodName).Append("Async(").Append(string.Join(", ", methodParameters)).AppendLine(")");
            }

            writer.AppendLine("{");
            using var _ = writer.PushIndent();
            var usesPathBuilder = pathParameters.Count > 0 || queryParameters.Count > 0 || _querySecuritySchemes.Count > 0;
            if (usesPathBuilder)
            {
                writer.AppendLine("var pathBuilder = new StringBuilder();");
                EmitRouteTemplate(writer, route, pathParameters);
            }
            else
            {
                writer.Append("var path = \"").Append(EscapeStringLiteral(route)).AppendLine("\";");
            }

            if (queryParameters.Count > 0 || _querySecuritySchemes.Count > 0)
            {
                writer.AppendLine("var hasQuery = false;");
                foreach (var parameter in queryParameters)
                {
                    if (parameter.Required)
                    {
                        writer.Append("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(EscapeStringLiteral(parameter.SerializedName)).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameter.ParameterName).AppendLine("));");
                    }
                    else
                    {
                        writer.Append("if (").Append(parameter.ParameterName).AppendLine(" is not null)");
                        writer.AppendLine("{");
                        using (writer.PushIndent())
                        {
                            writer.Append("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(EscapeStringLiteral(parameter.SerializedName)).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameter.ParameterName).AppendLine("));");
                        }

                        writer.AppendLine("}");
                    }
                }
            }

            foreach (var securityScheme in _querySecuritySchemes)
            {
                writer.Append("if (").Append(securityScheme.FieldName).AppendLine(" is not null)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.Append("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(EscapeStringLiteral(securityScheme.HeaderOrParameterName)).Append("\", ").Append(securityScheme.FieldName).AppendLine(");");
                }

                writer.AppendLine("}");
            }

            if (usesPathBuilder)
            {
                writer.AppendLine("var path = pathBuilder.ToString();");
            }

            writer.Append("using var request = new HttpRequestMessage(").Append(GetHttpMethodExpression(operation.OperationType)).AppendLine(", new Uri(path, UriKind.Relative));");

            foreach (var parameter in headerParameters)
            {
                writer.Append("if (").Append(parameter.ParameterName).AppendLine(" is not null)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.Append("request.Headers.TryAddWithoutValidation(\"").Append(parameter.SerializedName).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameter.ParameterName).AppendLine("));");
                }

                writer.AppendLine("}");
            }

            foreach (var securityScheme in model.SecuritySchemes)
            {
                if (securityScheme.Location == SecuritySchemeLocation.Query)
                {
                    continue;
                }

                writer.Append("if (").Append(securityScheme.FieldName).AppendLine(" is not null)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    if (securityScheme.IsBearerToken)
                    {
                        writer.Append("request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", ").Append(securityScheme.FieldName).AppendLine(");");
                    }
                    else if (securityScheme.Location == SecuritySchemeLocation.Cookie)
                    {
                        writer.Append("request.Headers.TryAddWithoutValidation(\"Cookie\", \"").Append(securityScheme.HeaderOrParameterName).Append("=\" + ").Append(securityScheme.FieldName).AppendLine(");");
                    }
                    else
                    {
                        writer.Append("request.Headers.TryAddWithoutValidation(\"").Append(securityScheme.HeaderOrParameterName).Append("\", ").Append(securityScheme.FieldName).AppendLine(");");
                    }
                }

                writer.AppendLine("}");
            }

            if (operation.RequestBody is not null)
            {
                if (operation.RequestBody.IsRequired)
                {
                    EmitRequestBodyContentAssignment(writer, operation.RequestBody, nullableBody: false);
                }
                else
                {
                    writer.AppendLine("if (body is not null)");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        EmitRequestBodyContentAssignment(writer, operation.RequestBody, nullableBody: true);
                    }

                    writer.AppendLine("}");
                }
            }

            writer.AppendLine("using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);");
            writer.AppendLine("if (!response.IsSuccessStatusCode)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                EmitErrorResponseHandling(writer, operation);
            }

            writer.AppendLine("}");

            if (operation.Response.Kind == ResponseKind.None)
            {
                writer.AppendLine("return;");
            }
            else if (operation.Response.Kind == ResponseKind.String)
            {
                writer.AppendLine("return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);");
            }
            else if (operation.Response.Kind == ResponseKind.Binary)
            {
                writer.AppendLine("return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);");
            }
            else if (RequiresNonNullJsonResponse(operation.Response.TypeName))
            {
                writer.Append("return await response.Content.ReadFromJsonAsync<").Append(operation.Response.TypeName).AppendLine(">(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false)");
                using (writer.PushIndent())
                {
                    writer.AppendLine("?? throw new OpenApiException((int)response.StatusCode, response.ReasonPhrase, response.Content?.Headers?.ContentType?.MediaType, null);");
                }
            }
            else
            {
                writer.Append("return await response.Content.ReadFromJsonAsync<").Append(operation.Response.TypeName).AppendLine(">(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false);");
            }

            writer.AppendLine("}");
        }

        private static void EmitErrorResponseHandling(IndentedStringBuilder writer, OperationGroupItem operation)
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

        private static void EmitTypedErrorResponseHandling(IndentedStringBuilder writer, ResponseInfo response)
        {
            var errorTypeName = TrimNullableTypeName(response.TypeName);
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
                            writer.Append("var error = OpenApiClientHelpers.DeserializeResponseContent<").Append(errorTypeName).AppendLine(">(responseContent);");
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

        private void EmitRouteTemplate(IndentedStringBuilder writer, string route, IReadOnlyList<ParameterInfo> pathParameters)
        {
            var parameterLookup = pathParameters
                .Where(static parameter => !string.IsNullOrEmpty(parameter.SerializedName))
                .ToDictionary(static parameter => parameter.SerializedName, StringComparer.Ordinal);

            var startIndex = 0;
            while (startIndex < route.Length)
            {
                var openBraceIndex = route.IndexOf('{', startIndex);
                if (openBraceIndex < 0)
                {
                    EmitRouteLiteral(writer, route.Substring(startIndex));
                    break;
                }

                var closeBraceIndex = route.IndexOf('}', openBraceIndex + 1);
                if (closeBraceIndex < 0)
                {
                    EmitRouteLiteral(writer, route.Substring(startIndex));
                    break;
                }

                EmitRouteLiteral(writer, route.Substring(startIndex, openBraceIndex - startIndex));

                var parameterName = route.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
                if (parameterLookup.TryGetValue(parameterName, out var parameter))
                {
                    writer.Append("pathBuilder.Append(Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter(")
                        .Append(parameter.ParameterName)
                        .AppendLine(")));");
                }
                else
                {
                    EmitRouteLiteral(writer, route.Substring(openBraceIndex, closeBraceIndex - openBraceIndex + 1));
                }

                startIndex = closeBraceIndex + 1;
            }
        }

        private static void EmitRouteLiteral(IndentedStringBuilder writer, string segment)
        {
            if (segment.Length == 0)
            {
                return;
            }

            writer.Append("pathBuilder.Append(\"").Append(EscapeStringLiteral(segment)).AppendLine("\");");
        }

        private static string NormalizeRelativeRoute(string route)
        {
            return route.TrimStart('/');
        }
    }
}
