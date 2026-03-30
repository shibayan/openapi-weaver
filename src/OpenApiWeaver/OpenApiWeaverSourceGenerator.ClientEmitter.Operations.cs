using System.Text;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class ClientEmitter
    {
        private void EmitOperation(StringBuilder builder, OperationGroupItem operation)
        {
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
                builder,
                "        ",
                summary: operation.Summary,
                remarks: operation.Remarks,
                parameters: parameterDocumentation,
                returns: operation.Response.Kind == ResponseKind.None ? null : operation.Response.Documentation);

            if (operation.Response.Kind == ResponseKind.None)
            {
                builder.Append("    public async Task ").Append(operation.MethodName).Append("Async(").Append(string.Join(", ", methodParameters)).AppendLine(")");
            }
            else
            {
                builder.Append("    public async Task<").Append(operation.Response.TypeName).Append("> ").Append(operation.MethodName).Append("Async(").Append(string.Join(", ", methodParameters)).AppendLine(")");
            }

            builder.AppendLine("    {");
            var usesPathBuilder = pathParameters.Count > 0 || queryParameters.Count > 0 || _querySecuritySchemes.Count > 0;
            if (usesPathBuilder)
            {
                builder.AppendLine("        var pathBuilder = new StringBuilder();");
                EmitRouteTemplate(builder, operation.Route, pathParameters);
            }
            else
            {
                builder.Append("        var path = \"").Append(EscapeStringLiteral(operation.Route)).AppendLine("\";");
            }

            if (queryParameters.Count > 0 || _querySecuritySchemes.Count > 0)
            {
                builder.AppendLine("        var hasQuery = false;");
                foreach (var parameter in queryParameters)
                {
                    if (parameter.Required)
                    {
                        builder.Append("        OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(EscapeStringLiteral(parameter.SerializedName)).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameter.ParameterName).AppendLine("));");
                    }
                    else
                    {
                        builder.Append("        if (").Append(parameter.ParameterName).AppendLine(" is not null)");
                        builder.AppendLine("        {");
                        builder.Append("            OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(EscapeStringLiteral(parameter.SerializedName)).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameter.ParameterName).AppendLine("));");
                        builder.AppendLine("        }");
                    }
                }
            }

            foreach (var securityScheme in _querySecuritySchemes)
            {
                builder.Append("        if (").Append(securityScheme.FieldName).AppendLine(" is not null)");
                builder.AppendLine("        {");
                builder.Append("            OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(EscapeStringLiteral(securityScheme.HeaderOrParameterName)).Append("\", ").Append(securityScheme.FieldName).AppendLine(");");
                builder.AppendLine("        }");
            }

            if (usesPathBuilder)
            {
                builder.AppendLine("        var path = pathBuilder.ToString();");
            }

            builder.Append("        using var request = new HttpRequestMessage(").Append(GetHttpMethodExpression(operation.OperationType)).AppendLine(", new Uri(path, UriKind.Relative));");

            foreach (var parameter in headerParameters)
            {
                builder.Append("        if (").Append(parameter.ParameterName).AppendLine(" is not null)");
                builder.AppendLine("        {");
                builder.Append("            request.Headers.TryAddWithoutValidation(\"").Append(parameter.SerializedName).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameter.ParameterName).AppendLine("));");
                builder.AppendLine("        }");
            }

            if (operation.RequestBody is not null)
            {
                if (operation.RequestBody.IsRequired)
                {
                    EmitRequestBodyContentAssignment(builder, operation.RequestBody, nullableBody: false);
                }
                else
                {
                    builder.AppendLine("        if (body is not null)");
                    builder.AppendLine("        {");
                    EmitRequestBodyContentAssignment(builder, operation.RequestBody, nullableBody: true);
                    builder.AppendLine("        }");
                }
            }

            builder.AppendLine("        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("        response.EnsureSuccessStatusCode();");

            if (operation.Response.Kind == ResponseKind.None)
            {
                builder.AppendLine("        return;");
            }
            else if (operation.Response.Kind == ResponseKind.String)
            {
                builder.AppendLine("        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);");
            }
            else if (operation.Response.Kind == ResponseKind.Binary)
            {
                builder.AppendLine("        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);");
            }
            else if (RequiresNonNullJsonResponse(operation.Response.TypeName))
            {
                builder.Append("        return await response.Content.ReadFromJsonAsync<").Append(operation.Response.TypeName).AppendLine(">(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false)");
                builder.AppendLine("            ?? throw new InvalidOperationException(\"The response body was empty.\");");
            }
            else
            {
                builder.Append("        return await response.Content.ReadFromJsonAsync<").Append(operation.Response.TypeName).AppendLine(">(OpenApiClientHelpers.SerializerOptions, cancellationToken).ConfigureAwait(false);");
            }

            builder.AppendLine("    }");
        }

        private void EmitRouteTemplate(StringBuilder builder, string route, IReadOnlyList<ParameterInfo> pathParameters)
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
                    builder.AppendLine($"        pathBuilder.Append(Uri.EscapeDataString(OpenApiClientHelpers.FormatParameter({parameter.ParameterName})));");
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
    }
}
