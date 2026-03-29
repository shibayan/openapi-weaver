using System.Globalization;
using System.Text;

using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class ClientEmitter
    {
        private void EmitOperation(StringBuilder builder, string route, string operationType, OpenApiOperation operation, List<IOpenApiParameter> parameters)
        {
            var tagName = GetTagName(operation);
            var methodName = BuildOperationMethodName(operation.OperationId, operationType, route, tagName);
            var hasHeaderParameters = parameters.Any(x => x.In == ParameterLocation.Header);

            var requestBody = ResolveRequestBody(operation.RequestBody);
            var response = ResolveResponse(operation.Responses ?? []);
            var parameterDocumentation = new List<KeyValuePair<string, string?>>();
            var pathParameters = parameters.Where(static parameter => parameter.In == ParameterLocation.Path).ToList();
            var queryParameters = parameters.Where(static parameter => parameter.In is ParameterLocation.Query or ParameterLocation.QueryString).ToList();

            var requiredMethodParameters = new List<string>();
            var optionalMethodParameters = new List<string>();

            foreach (var parameter in parameters)
            {
                var parameterName = SafeIdentifier(ToCamelCase(parameter.Name ?? string.Empty));
                var parameterDeclaration = $"{ResolveTypeName(parameter.Schema, parameter.Required)} {parameterName}";
                if (parameter.Required)
                {
                    requiredMethodParameters.Add(parameterDeclaration);
                }
                else
                {
                    optionalMethodParameters.Add($"{parameterDeclaration} = default");
                }

                parameterDocumentation.Add(new KeyValuePair<string, string?>(parameterName, parameter.Description));
            }

            if (requestBody is not null)
            {
                var bodyTypeName = requestBody.IsRequired ? requestBody.TypeName : MakeNullableTypeName(requestBody.TypeName);
                var bodyParameter = $"{bodyTypeName} body";
                if (requestBody.IsRequired)
                {
                    requiredMethodParameters.Add(bodyParameter);
                }
                else
                {
                    optionalMethodParameters.Add($"{bodyParameter} = default");
                }

                parameterDocumentation.Add(new KeyValuePair<string, string?>("body", operation.RequestBody?.Description));
            }

            var methodParameters = requiredMethodParameters
                .Concat(optionalMethodParameters)
                .Append("CancellationToken cancellationToken = default")
                .ToList();
            parameterDocumentation.Add(new KeyValuePair<string, string?>("cancellationToken", "A cancellation token that can be used to cancel the operation."));

            EmitDocComment(
                builder,
                "        ",
                summary: operation.Summary ?? operation.Description ?? $"{ToPascalCase(operationType.ToLowerInvariant())} {route}.",
                remarks: operation.Summary is not null && !string.IsNullOrWhiteSpace(operation.Description) ? operation.Description : null,
                parameters: parameterDocumentation,
                returns: response.Kind == ResponseKind.None ? null : ResolveResponseDocumentation(operation));

            if (response.Kind == ResponseKind.None)
            {
                builder.Append("    public async Task ").Append(methodName).Append("Async(").Append(string.Join(", ", methodParameters)).AppendLine(")");
            }
            else
            {
                builder.Append("    public async Task<").Append(response.TypeName).Append("> ").Append(methodName).Append("Async(").Append(string.Join(", ", methodParameters)).AppendLine(")");
            }

            builder.AppendLine("    {");
            var usesPathBuilder = pathParameters.Count > 0 || queryParameters.Count > 0 || _querySecuritySchemes.Count > 0;
            if (usesPathBuilder)
            {
                builder.AppendLine("        var pathBuilder = new StringBuilder();");
                EmitRouteTemplate(builder, route, pathParameters);
            }
            else
            {
                builder.Append("        var path = \"").Append(EscapeStringLiteral(route)).AppendLine("\";");
            }

            if (queryParameters.Count > 0 || _querySecuritySchemes.Count > 0)
            {
                builder.AppendLine("        var hasQuery = false;");
                foreach (var parameter in queryParameters)
                {
                    var parameterName = SafeIdentifier(ToCamelCase(parameter.Name ?? string.Empty));
                    if (parameter.Required)
                    {
                        builder.Append("        OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(parameter.Name).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameterName).AppendLine("));");
                    }
                    else
                    {
                        builder.Append("        if (").Append(parameterName).AppendLine(" is not null)");
                        builder.AppendLine("        {");
                        builder.Append("            OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(parameter.Name).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameterName).AppendLine("));");
                        builder.AppendLine("        }");
                    }
                }
            }

            foreach (var securityScheme in _querySecuritySchemes)
            {
                builder.Append("        if (").Append(securityScheme.FieldName).AppendLine(" is not null)");
                builder.AppendLine("        {");
                builder.Append("            OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(securityScheme.HeaderOrParameterName).Append("\", ").Append(securityScheme.FieldName).AppendLine(");");
                builder.AppendLine("        }");
            }

            if (usesPathBuilder)
            {
                builder.AppendLine("        var path = pathBuilder.ToString();");
            }

            var httpMethodExpression = GetHttpMethodExpression(operationType);
            builder.Append("        using var request = new HttpRequestMessage(").Append(httpMethodExpression).AppendLine(", new Uri(path, UriKind.Relative));");

            if (hasHeaderParameters)
            {
                foreach (var parameter in parameters)
                {
                    if (parameter.In != ParameterLocation.Header)
                    {
                        continue;
                    }

                    var parameterName = SafeIdentifier(ToCamelCase(parameter.Name ?? string.Empty));
                    builder.Append("        if (").Append(parameterName).AppendLine(" is not null)");
                    builder.AppendLine("        {");
                    builder.Append("            request.Headers.TryAddWithoutValidation(\"").Append(parameter.Name).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameterName).AppendLine("));");
                    builder.AppendLine("        }");
                }
            }

            if (requestBody is not null)
            {
                if (requestBody.IsRequired)
                {
                    EmitRequestBodyContentAssignment(builder, requestBody, nullableBody: false);
                }
                else
                {
                    builder.AppendLine("        if (body is not null)");
                    builder.AppendLine("        {");
                    EmitRequestBodyContentAssignment(builder, requestBody, nullableBody: true);
                    builder.AppendLine("        }");
                }
            }

            builder.AppendLine("        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        response.EnsureSuccessStatusCode();");

            if (response.Kind == ResponseKind.None)
            {
                builder.AppendLine("        return;");
            }
            else if (response.Kind == ResponseKind.String)
            {
                builder.AppendLine("        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);");
            }
            else if (response.Kind == ResponseKind.Binary)
            {
                builder.AppendLine("        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);");
            }
            else if (RequiresNonNullJsonResponse(response.TypeName))
            {
                builder.Append("        return await response.Content.ReadFromJsonAsync<").Append(response.TypeName).AppendLine(">(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false)");
                builder.AppendLine("            ?? throw new InvalidOperationException(\"The response body was empty.\");");
            }
            else
            {
                builder.Append("        return await response.Content.ReadFromJsonAsync<").Append(response.TypeName).AppendLine(">(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false);");
            }

            builder.AppendLine("    }");
        }

        private void EmitRouteTemplate(StringBuilder builder, string route, IReadOnlyList<IOpenApiParameter> pathParameters)
        {
            var parameterLookup = pathParameters
                .Where(static parameter => !string.IsNullOrEmpty(parameter.Name))
                .ToDictionary(static parameter => parameter.Name!, StringComparer.Ordinal);

            var startIndex = 0;
            while (startIndex < route.Length)
            {
                var openBraceIndex = route.IndexOf('{', startIndex);
                if (openBraceIndex < 0)
                {
                    EmitRouteLiteral(builder, route.Substring(startIndex));
                    break;
                }

                var closeBraceIndex = route.IndexOf('}', openBraceIndex + 1);
                if (closeBraceIndex < 0)
                {
                    EmitRouteLiteral(builder, route.Substring(startIndex));
                    break;
                }

                EmitRouteLiteral(builder, route.Substring(startIndex, openBraceIndex - startIndex));

                var parameterName = route.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
                if (parameterLookup.TryGetValue(parameterName, out var parameter))
                {
                    var pathParameterName = SafeIdentifier(ToCamelCase(parameter.Name ?? string.Empty));
                    builder.AppendLine($"        pathBuilder.Append(Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter({pathParameterName})));");
                }
                else
                {
                    EmitRouteLiteral(builder, route.Substring(openBraceIndex, closeBraceIndex - openBraceIndex + 1));
                }

                startIndex = closeBraceIndex + 1;
            }
        }

        private static void EmitRouteLiteral(StringBuilder builder, string segment)
        {
            if (segment.Length == 0)
            {
                return;
            }

            builder.Append("        pathBuilder.Append(\"").Append(EscapeStringLiteral(segment)).AppendLine("\");");
        }

        private RequestBodyInfo? ResolveRequestBody(IOpenApiRequestBody? requestBody)
        {
            if (requestBody?.Content is null || requestBody.Content.Count == 0)
            {
                return null;
            }

            var selectedContent = SelectPreferredContent(
                requestBody.Content,
                static item => string.Equals(item.Key, "application/json", StringComparison.OrdinalIgnoreCase) ? 0 :
                    string.Equals(item.Key, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ? 1 :
                    string.Equals(item.Key, "multipart/form-data", StringComparison.OrdinalIgnoreCase) ? 2 :
                    int.MaxValue);

            return new RequestBodyInfo(
                ResolveRequestBodyKind(selectedContent.Key),
                ResolveTypeName(selectedContent.Value.Schema, requestBody.Required),
                requestBody.Required,
                selectedContent.Value.Schema);
        }

        private ResponseInfo ResolveResponse(OpenApiResponses responses)
        {
            var response = SelectSuccessResponse(responses);

            if (response?.Content is null || response.Content.Count == 0)
            {
                return new ResponseInfo(ResponseKind.None, string.Empty);
            }

            var selectedContent = SelectPreferredContent(
                response.Content,
                static item => item.Key.Contains("json", StringComparison.OrdinalIgnoreCase) ? 0 :
                    HasSchemaType(item.Value.Schema, JsonSchemaType.String) && string.Equals(item.Value.Schema?.Format, "binary", StringComparison.OrdinalIgnoreCase) ? 1 :
                    item.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ? 2 :
                    int.MaxValue);

            var kind = ResolveResponseKind(selectedContent.Key, selectedContent.Value.Schema);
            var typeName = kind switch
            {
                ResponseKind.Binary => "byte[]",
                ResponseKind.String => "string",
                ResponseKind.None => string.Empty,
                _ => ResolveTypeName(selectedContent.Value.Schema, required: selectedContent.Value.Schema is null || !IsNullableSchema(selectedContent.Value.Schema))
            };

            return new ResponseInfo(kind, typeName);
        }

        private static RequestBodyKind ResolveRequestBodyKind(string contentType)
        {
            if (string.Equals(contentType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                return RequestBodyKind.FormUrlEncoded;
            }

            if (string.Equals(contentType, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                return RequestBodyKind.MultipartFormData;
            }

            return RequestBodyKind.Json;
        }

        private static ResponseKind ResolveResponseKind(string contentType, IOpenApiSchema? schema)
        {
            if (HasSchemaType(schema, JsonSchemaType.String) && string.Equals(schema?.Format, "binary", StringComparison.OrdinalIgnoreCase))
            {
                return ResponseKind.Binary;
            }

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return ResponseKind.Json;
            }

            return ResponseKind.String;
        }

        private static int ParseResponseStatusCode(string statusCode)
        {
            return int.TryParse(statusCode, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : int.MaxValue;
        }

        private static string? ResolveResponseDocumentation(OpenApiOperation operation)
        {
            if (operation.Responses is null)
            {
                return null;
            }

            var response = SelectSuccessResponse(operation.Responses);
            if (response is null)
            {
                return null;
            }

            return !string.IsNullOrWhiteSpace(response.Summary) ? response.Summary : response.Description;
        }

        private static IOpenApiResponse? SelectSuccessResponse(OpenApiResponses responses)
        {
            IOpenApiResponse? selectedResponse = null;
            var bestStatusCode = int.MaxValue;
            var selectedHasUsableContent = false;

            foreach (var item in responses)
            {
                if (!item.Key.StartsWith("2", StringComparison.Ordinal))
                {
                    continue;
                }

                var statusCode = ParseResponseStatusCode(item.Key);
                var hasUsableContent = HasUsableResponseContent(item.Value);

                if (selectedResponse is null
                    || (hasUsableContent && !selectedHasUsableContent)
                    || (hasUsableContent == selectedHasUsableContent && statusCode < bestStatusCode))
                {
                    selectedResponse = item.Value;
                    bestStatusCode = statusCode;
                    selectedHasUsableContent = hasUsableContent;
                }
            }

            return selectedResponse;
        }

        private static bool HasUsableResponseContent(IOpenApiResponse response)
        {
            if (response.Content is null || response.Content.Count == 0)
            {
                return false;
            }

            foreach (var mediaType in response.Content)
            {
                if (IsUsableResponseMediaType(mediaType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUsableResponseMediaType(KeyValuePair<string, IOpenApiMediaType> mediaType)
        {
            if (mediaType.Value.Schema is not null)
            {
                return true;
            }

            return mediaType.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
