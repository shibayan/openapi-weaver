namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Emitter
    {
        private void EmitOperation(IndentedStringBuilder writer, OperationGroupItem operation)
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

            CSharpCodeEmissionUtilities.EmitDocComment(
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
            using var _ = writer.PushIndent();
            EmitSecurityRequirementSelection(writer, operation);
            var usesPathBuilder = pathParameters.Count > 0 || queryParameters.Count > 0 || querySecuritySchemes.Count > 0;
            if (usesPathBuilder)
            {
                writer.AppendLine("var pathBuilder = new StringBuilder();");
                EmitRouteTemplate(writer, route, pathParameters);
            }
            else
            {
                writer.Append("var path = \"").Append(CSharpCodeEmissionUtilities.EscapeStringLiteral(route)).AppendLine("\";");
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
                    writer.Append("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(CSharpCodeEmissionUtilities.EscapeStringLiteral(securityScheme.HeaderOrParameterName)).Append("\", ").Append(securityScheme.FieldName).AppendLine(");");
                });
            }

            if (usesPathBuilder)
            {
                writer.AppendLine("var path = pathBuilder.ToString();");
            }

            writer.Append("using var request = new HttpRequestMessage(").Append(CSharpCodeEmissionUtilities.GetHttpMethodExpression(operation.OperationType)).AppendLine(", new Uri(path, UriKind.Relative));");

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
                        writer.Append("OpenApiClientHelpers.AppendCookieParameter(cookieBuilder, \"").Append(CSharpCodeEmissionUtilities.EscapeStringLiteral(securityScheme.HeaderOrParameterName)).Append("\", ").Append(securityScheme.FieldName).AppendLine(");");
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

                    writer.Append("request.Headers.TryAddWithoutValidation(\"").Append(securityScheme.HeaderOrParameterName).Append("\", ").Append(securityScheme.FieldName).AppendLine(");");
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
                    EmitRequestBodyContentAssignment(writer, operation.RequestBody, nullableBody: false);
                }
                else
                {
                    writer.Append("if (").Append(operation.RequestBody.ParameterName).AppendLine(" is not null)");
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
            else if (operation.Response.Type?.RequiresNonNullJsonResponse == true)
            {
                writer.Append("return await response.Content.ReadFromJsonAsync<").Append(operation.Response.ResponseTypeName).Append(">(").Append(_jsonSerializerOptionsEmitter.GetOptionsExpression(JsonSerializerDirection.Response)).AppendLine(", cancellationToken).ConfigureAwait(false)");
                using (writer.PushIndent())
                {
                    writer.AppendLine("?? throw new OpenApiException((int)response.StatusCode, response.ReasonPhrase, response.Content?.Headers?.ContentType?.MediaType, null);");
                }
            }
            else
            {
                writer.Append("return await response.Content.ReadFromJsonAsync<").Append(operation.Response.ResponseTypeName).Append(">(").Append(_jsonSerializerOptionsEmitter.GetOptionsExpression(JsonSerializerDirection.Response)).AppendLine(", cancellationToken).ConfigureAwait(false);");
            }

            writer.AppendLine("}");
        }

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
                writer.Append("if (OpenApiClientHelpers.ResponseMatchesStatusCode(statusCode, \"").Append(CSharpCodeEmissionUtilities.EscapeStringLiteral(errorResponse.StatusCodePattern)).AppendLine("\"))");
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
                            var serializerOptions = _jsonSerializerOptionsEmitter.GetOptionsExpression(JsonSerializerDirection.Response);
                            writer.Append("var error = OpenApiClientHelpers.DeserializeResponseContent<").Append(errorTypeName).Append(">(responseContent");
                            if (!string.Equals(serializerOptions, "OpenApiClientHelpers.SerializerOptions", StringComparison.Ordinal))
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

        private void EmitRouteTemplate(IndentedStringBuilder writer, string route, IReadOnlyList<ParameterInfo> pathParameters)
        {
            var parameterLookup = pathParameters
                .Where(static parameter => !string.IsNullOrEmpty(parameter.WireName))
                .ToDictionary(static parameter => parameter.WireName, StringComparer.Ordinal);

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
                    writer.Append("pathBuilder.Append(Uri.EscapeDataString(");
                    EmitParameterValue(writer, parameter);
                    writer.AppendLine("));");
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

            writer.Append("pathBuilder.Append(\"").Append(CSharpCodeEmissionUtilities.EscapeStringLiteral(segment)).AppendLine("\");");
        }

        private static string NormalizeRelativeRoute(string route)
        {
            return route.TrimStart('/');
        }

        private static void EmitQueryParameterAppend(IndentedStringBuilder writer, ParameterInfo parameter)
        {
            if (parameter.IsArray)
            {
                writer.Append("OpenApiClientHelpers.AppendQueryParameters(pathBuilder, ref hasQuery, \"").Append(CSharpCodeEmissionUtilities.EscapeStringLiteral(parameter.WireName)).Append("\", ").Append(parameter.ParameterName).AppendLine(");");
                return;
            }

            writer.Append("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(CSharpCodeEmissionUtilities.EscapeStringLiteral(parameter.WireName)).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameter.ParameterName).AppendLine("));");
        }

        private static void EmitHeaderParameterAppend(IndentedStringBuilder writer, ParameterInfo parameter)
        {
            writer.Append("request.Headers.TryAddWithoutValidation(\"").Append(CSharpCodeEmissionUtilities.EscapeStringLiteral(parameter.WireName)).Append("\", ");
            EmitParameterValue(writer, parameter);
            writer.AppendLine(");");
        }

        private static void EmitCookieParameterAppend(IndentedStringBuilder writer, ParameterInfo parameter)
        {
            writer.Append("OpenApiClientHelpers.AppendCookieParameter(cookieBuilder, \"").Append(CSharpCodeEmissionUtilities.EscapeStringLiteral(parameter.WireName)).Append("\", ");
            EmitParameterValue(writer, parameter);
            writer.AppendLine(");");
        }

        private static void EmitParameterValue(IndentedStringBuilder writer, ParameterInfo parameter)
        {
            if (parameter.IsArray)
            {
                writer.Append("OpenApiClientHelpers.FormatCollectionParameter(").Append(parameter.ParameterName).Append(')');
                return;
            }

            writer.Append("OpenApiClientHelpers.FormatParameter(").Append(parameter.ParameterName).Append(')');
        }

        private void EmitSecurityRequirementSelection(IndentedStringBuilder writer, OperationGroupItem operation)
        {
            if (operation.SecurityRequirements is not { Count: > 0 } securityRequirements
                || !securityRequirements.Any(static requirement => requirement.Schemes.Count > 0))
            {
                return;
            }

            writer.AppendLine("var securityRequirementIndex = 0;");
            for (var i = 0; i < securityRequirements.Count; i++)
            {
                writer.Append("if (securityRequirementIndex == 0");
                var requirement = securityRequirements[i];
                foreach (var scheme in requirement.Schemes)
                {
                    writer.Append(" && ").Append(scheme.FieldName).Append(" is not null");
                }

                writer.AppendLine(")");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    writer.Append("securityRequirementIndex = ").Append((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(";");
                }

                writer.AppendLine("}");
            }
        }

        private void EmitSecuritySchemeBlock(IndentedStringBuilder writer, OperationGroupItem operation, SecuritySchemeBinding securityScheme, Action emitBody)
        {
            if (operation.SecurityRequirements is null)
            {
                writer.Append("if (").Append(securityScheme.FieldName).AppendLine(" is not null)");
                writer.AppendLine("{");
                using (writer.PushIndent())
                {
                    emitBody();
                }

                writer.AppendLine("}");
                return;
            }

            var matchingRequirements = new List<int>();
            for (var i = 0; i < operation.SecurityRequirements.Count; i++)
            {
                if (operation.SecurityRequirements[i].Schemes.Contains(securityScheme))
                {
                    matchingRequirements.Add(i + 1);
                }
            }

            if (matchingRequirements.Count == 0)
            {
                return;
            }

            writer.Append("if (");
            for (var i = 0; i < matchingRequirements.Count; i++)
            {
                if (i > 0)
                {
                    writer.Append(" || ");
                }

                writer.Append("securityRequirementIndex == ").Append(matchingRequirements[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            writer.AppendLine(")");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                emitBody();
            }

            writer.AppendLine("}");
        }

        private List<SecuritySchemeBinding> GetOperationSecuritySchemes(OperationGroupItem operation, SecuritySchemeLocation location)
            => GetOperationSecuritySchemes(operation, static (scheme, state) => scheme.Location == state, location);

        private List<SecuritySchemeBinding> GetOperationSecuritySchemesExcept(OperationGroupItem operation, SecuritySchemeLocation location)
            => GetOperationSecuritySchemes(operation, static (scheme, state) => scheme.Location != state, location);

        private List<SecuritySchemeBinding> GetOperationSecuritySchemes<TState>(
            OperationGroupItem operation,
            Func<SecuritySchemeBinding, TState, bool> predicate,
            TState state)
        {
            var schemes = operation.SecurityRequirements is null
                ? model.SecuritySchemes
                : operation.SecurityRequirements.SelectMany(static requirement => requirement.Schemes).ToList();

            var result = new List<SecuritySchemeBinding>();
            var usedSchemeKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scheme in schemes)
            {
                if (predicate(scheme, state) && usedSchemeKeys.Add(scheme.SchemeKey))
                {
                    result.Add(scheme);
                }
            }

            return result;
        }
    }
}
