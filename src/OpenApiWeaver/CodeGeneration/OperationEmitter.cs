using static OpenApiWeaver.CodeGeneration.CSharpCodeEmissionUtilities;

namespace OpenApiWeaver.CodeGeneration;

internal sealed partial class OperationEmitter(ClientModel model)
{
    private readonly ClientModel _model = model;
    private readonly OperationRequestBodyEmitter _requestBodyEmitter = new(model);

    public void Emit(IndentedStringBuilder writer, OperationGroupItem operation)
    {
        var route = NormalizeRelativeRoute(operation.Route);
        var parameterDocumentation = new List<KeyValuePair<string, string?>>();
        var pathParameters = new List<ParameterInfo>();
        var queryParameters = new List<ParameterInfo>();
        var headerParameters = new List<ParameterInfo>();
        var cookieParameters = new List<ParameterInfo>();
        var querySecuritySchemes = GetOperationSecuritySchemes(operation, SecuritySchemeLocation.Query);
        var cookieSecuritySchemes = GetOperationSecuritySchemes(operation, SecuritySchemeLocation.Cookie);

        var requiredMethodParameters = new List<string>();
        var optionalMethodParameters = new List<string>();

        foreach (var parameter in operation.Parameters)
        {
            switch (parameter.Location)
            {
                case ParameterLocation.Path:
                    pathParameters.Add(parameter);
                    break;
                case ParameterLocation.Header:
                    headerParameters.Add(parameter);
                    break;
                case ParameterLocation.Cookie:
                    cookieParameters.Add(parameter);
                    break;
                default:
                    queryParameters.Add(parameter);
                    break;
            }

            var parameterDeclaration = $"{parameter.ParameterTypeName} {parameter.ParameterName}";
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
            var bodyParameter = $"{operation.RequestBody.BodyTypeName} {operation.RequestBody.ParameterName}";
            if (operation.RequestBody.IsRequired)
            {
                requiredMethodParameters.Add(bodyParameter);
            }
            else
            {
                optionalMethodParameters.Add($"{bodyParameter} = default");
            }

            parameterDocumentation.Add(new KeyValuePair<string, string?>(operation.RequestBody.ParameterName, operation.RequestBody.Description));
        }

        var methodParameters = new List<string>(requiredMethodParameters.Count + optionalMethodParameters.Count + 1);
        methodParameters.AddRange(requiredMethodParameters);
        methodParameters.AddRange(optionalMethodParameters);
        methodParameters.Add("CancellationToken cancellationToken = default");
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
            writer.Append("public async Task<").Append(operation.Response.ResponseTypeName).Append("> ").Append(operation.MethodName).Append("Async(").Append(string.Join(", ", methodParameters)).AppendLine(")");
        }

        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            EmitSecurityRequirementSelection(writer, operation);
            var usesPathBuilder = pathParameters.Count > 0 || queryParameters.Count > 0 || querySecuritySchemes.Count > 0;
            if (usesPathBuilder)
            {
                writer.AppendLine("var pathBuilder = new StringBuilder();");
                EmitRouteTemplate(writer, route, pathParameters);
            }
            else
            {
                writer.Append("var path = \"").Append(EscapeStringLiteral(route)).AppendLine("\";");
            }

            if (queryParameters.Count > 0 || querySecuritySchemes.Count > 0)
            {
                writer.AppendLine("var hasQuery = false;");
                foreach (var parameter in queryParameters)
                {
                    if (parameter.Required)
                    {
                        EmitQueryParameterAppend(writer, parameter);
                    }
                    else
                    {
                        writer.Append("if (").Append(parameter.ParameterName).AppendLine(" is not null)");
                        writer.AppendLine("{");
                        using (writer.PushIndent())
                        {
                            EmitQueryParameterAppend(writer, parameter);
                        }

                        writer.AppendLine("}");
                    }
                }
            }

            foreach (var securityScheme in querySecuritySchemes)
            {
                EmitSecuritySchemeBlock(writer, operation, securityScheme, () =>
                {
                    writer.Append("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(EscapeStringLiteral(Uri.EscapeDataString(securityScheme.HeaderOrParameterName))).Append("\", ").Append(securityScheme.FieldName).AppendLine(");");
                });
            }

            if (usesPathBuilder)
            {
                writer.AppendLine("var path = pathBuilder.ToString();");
            }

            writer.Append("using var request = new HttpRequestMessage(").Append(GetHttpMethodExpression(operation.OperationType)).AppendLine(", new Uri(path, UriKind.Relative));");

            if (cookieParameters.Count > 0 || cookieSecuritySchemes.Count > 0)
            {
                writer.AppendLine("var cookieBuilder = new StringBuilder();");
            }

            foreach (var parameter in headerParameters)
            {
                if (parameter.Required)
                {
                    EmitHeaderParameterAppend(writer, parameter);
                }
                else
                {
                    writer.Append("if (").Append(parameter.ParameterName).AppendLine(" is not null)");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        EmitHeaderParameterAppend(writer, parameter);
                    }

                    writer.AppendLine("}");
                }
            }

            foreach (var parameter in cookieParameters)
            {
                if (parameter.Required)
                {
                    EmitCookieParameterAppend(writer, parameter);
                }
                else
                {
                    writer.Append("if (").Append(parameter.ParameterName).AppendLine(" is not null)");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        EmitCookieParameterAppend(writer, parameter);
                    }

                    writer.AppendLine("}");
                }
            }

            foreach (var securityScheme in GetOperationSecuritySchemesExcept(operation, SecuritySchemeLocation.Query))
            {
                if (securityScheme.Location == SecuritySchemeLocation.Cookie)
                {
                    EmitSecuritySchemeBlock(writer, operation, securityScheme, () =>
                    {
                        writer.Append("OpenApiClientHelpers.AppendCookieParameter(cookieBuilder, \"").Append(EscapeStringLiteral(Uri.EscapeDataString(securityScheme.HeaderOrParameterName))).Append("\", ").Append(securityScheme.FieldName).AppendLine(");");
                    });
                    continue;
                }

                EmitSecuritySchemeBlock(writer, operation, securityScheme, () =>
                {
                    if (securityScheme.IsBearerToken)
                    {
                        writer.Append("request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(\"Bearer\", ").Append(securityScheme.FieldName).AppendLine(");");
                        return;
                    }

                    writer.Append("request.Headers.TryAddWithoutValidation(\"").Append(EscapeStringLiteral(securityScheme.HeaderOrParameterName)).Append("\", ").Append(securityScheme.FieldName).AppendLine(");");
                });
            }

            if (cookieParameters.Count > 0 || cookieSecuritySchemes.Count > 0)
            {
                writer.AppendLine("if (cookieBuilder.Length > 0)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.AppendLine("request.Headers.TryAddWithoutValidation(\"Cookie\", cookieBuilder.ToString());");
                }

                writer.AppendLine("}");
            }

            if (operation.RequestBody is not null)
            {
                if (operation.RequestBody.IsRequired)
                {
                    _requestBodyEmitter.EmitContentAssignment(writer, operation.RequestBody, nullableBody: false);
                }
                else
                {
                    writer.Append("if (").Append(operation.RequestBody.ParameterName).AppendLine(" is not null)");
                    writer.AppendLine("{");
                    using (writer.PushIndent())
                    {
                        _requestBodyEmitter.EmitContentAssignment(writer, operation.RequestBody, nullableBody: true);
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
            else if (operation.Response.Type?.RequiresNonNullJsonResponse == true)
            {
                writer.Append("return await response.Content.ReadFromJsonAsync<").Append(operation.Response.ResponseTypeName).Append(">(").Append(GetSerializerOptionsExpression(JsonSerializerDirection.Response)).AppendLine(", cancellationToken).ConfigureAwait(false)");
                using (writer.PushIndent())
                {
                    writer.AppendLine("?? throw new OpenApiException((int)response.StatusCode, response.ReasonPhrase, response.Content?.Headers?.ContentType?.MediaType, null);");
                }
            }
            else
            {
                writer.Append("return await response.Content.ReadFromJsonAsync<").Append(operation.Response.ResponseTypeName).Append(">(").Append(GetSerializerOptionsExpression(JsonSerializerDirection.Response)).AppendLine(", cancellationToken).ConfigureAwait(false);");
            }
        }

        writer.AppendLine("}");
    }
}
