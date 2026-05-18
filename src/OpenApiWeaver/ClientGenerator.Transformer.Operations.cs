using System.Globalization;

using Microsoft.OpenApi;

using OpenApiParameterLocation = Microsoft.OpenApi.ParameterLocation;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private List<TagGroup> BuildTagGroups(IReadOnlyList<SecuritySchemeBinding> securitySchemes, string serializerOptionsTypeName)
        {
            var tagDescriptions = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_document.Tags is not null)
            {
                foreach (var tag in _document.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag.Name) && !string.IsNullOrWhiteSpace(tag.Description))
                    {
                        tagDescriptions[tag.Name!] = tag.Description!;
                    }
                }
            }

            var accumulators = new Dictionary<string, TagGroupAccumulator>(StringComparer.Ordinal);

            foreach (var path in _document.Paths)
            {
                foreach (var operation in path.Value.Operations ?? [])
                {
                    var tagName = GetTagName(operation.Value);
                    var groupName = string.IsNullOrWhiteSpace(tagName) ? "Default" : tagName!;

                    if (!accumulators.TryGetValue(groupName, out var accumulator))
                    {
                        tagDescriptions.TryGetValue(groupName, out var description);
                        accumulator = new TagGroupAccumulator(groupName, description);
                        accumulators[groupName] = accumulator;
                    }

                    accumulator.Operations.Add(BuildOperation(path.Key, operation.Key.ToString(), path.Value, operation.Value, accumulator.UsedMethodNames, securitySchemes));
                }
            }

            var usedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
            var reservedClassNames = _schemaNames.Values
                .Concat(_inlineSchemas.Where(static schema => schema.ParentTypeName is null).Select(static schema => schema.DeclaredTypeName))
                .Append(_clientName)
                .Append("OpenApiClientHelpers")
                .Append("OpenApiException");
            if (!string.IsNullOrWhiteSpace(serializerOptionsTypeName))
            {
                reservedClassNames = reservedClassNames.Append(serializerOptionsTypeName);
            }

            var usedClassNames = new HashSet<string>(
                reservedClassNames,
                StringComparer.Ordinal);

            return [..
                accumulators.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair =>
                    {
                        var propertyName = AllocateUniqueName(
                            usedPropertyNames,
                            NormalizePascalIdentifier(pair.Value.GroupName, "Default"),
                            "Default");
                        var className = AllocateUniqueName(
                            usedClassNames,
                            propertyName.EndsWith("Client", StringComparison.Ordinal) ? propertyName : propertyName + "Client",
                            "DefaultClient");
                        return new TagGroup(propertyName, className, pair.Value.Description, pair.Value.Operations);
                    })];
        }

        private sealed class TagGroupAccumulator(string groupName, string? description)
        {
            public string GroupName { get; } = groupName;
            public string? Description { get; } = description;
            public List<OperationGroupItem> Operations { get; } = [];
            public HashSet<string> UsedMethodNames { get; } = new(StringComparer.Ordinal);
        }

        private OperationGroupItem BuildOperation(
            string route,
            string operationType,
            IOpenApiPathItem pathItem,
            OpenApiOperation operation,
            ISet<string> usedMethodNames,
            IReadOnlyList<SecuritySchemeBinding> securitySchemes)
        {
            var tagName = GetTagName(operation);
            var usedParameterNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "cancellationToken"
            };
            var parameters = CollectParameters(pathItem, operation)
                .Select(parameter => new ParameterInfo(
                    parameter.Name ?? string.Empty,
                    AllocateUniqueName(
                        usedParameterNames,
                        NormalizeCamelIdentifier(parameter.Name ?? string.Empty, "value"),
                        "value"),
                    ResolveTypeUsage(parameter.Schema, parameter.Required),
                    parameter.Required,
                    MapParameterLocation(parameter.In ?? OpenApiParameterLocation.Query),
                    parameter.Description))
                .ToList();

            var requestBody = ResolveRequestBody(operation.RequestBody, usedParameterNames);
            var response = ResolveResponse(operation);
            var errorResponses = ResolveErrorResponses(operation);
            var operationSecurityRequirements = ResolveOperationSecurityRequirements(operation, securitySchemes);
            var methodName = AllocateUniqueName(
                usedMethodNames,
                BuildOperationMethodName(operation.OperationId, operationType, route, tagName),
                NormalizePascalIdentifier(operationType, "Operation"));

            return new OperationGroupItem(
                route,
                operationType,
                methodName,
                operation.Summary ?? operation.Description ?? $"{ToPascalCase(operationType.ToLowerInvariant())} {route}.",
                operation.Summary is not null && !string.IsNullOrWhiteSpace(operation.Description) ? operation.Description : null,
                parameters,
                requestBody,
                response,
                errorResponses,
                operationSecurityRequirements);
        }

        private ResponseInfo ResolveResponse(OpenApiOperation operation)
        {
            var response = SelectSuccessResponse(operation.Responses ?? []);
            if (response is null || !TrySelectPreferredContent(response.Content, GetResponseContentPriority, out var selectedContent))
            {
                return new ResponseInfo(ResponseKind.None, type: null, null);
            }

            var type = ResolveResponseTypeUsage(selectedContent.Key, selectedContent.Value.Schema);
            var kind = ResolveResponseKind(selectedContent.Key, type);

            return new ResponseInfo(kind, type, !string.IsNullOrWhiteSpace(response.Summary) ? response.Summary : response.Description);
        }

        private List<ErrorResponseInfo> ResolveErrorResponses(OpenApiOperation operation)
        {
            if (operation.Responses is null || operation.Responses.Count == 0)
            {
                return [];
            }

            var hasSuccessStatus = operation.Responses.Any(static item => IsSuccessResponseStatus(item.Key));

            var errorResponses = new List<ErrorResponseInfo>();
            foreach (var item in operation.Responses)
            {
                if (IsSuccessResponseStatus(item.Key))
                {
                    continue;
                }

                if (!hasSuccessStatus && string.Equals(item.Key, "default", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var response = ResolveErrorResponse(item.Value);
                if (response is null)
                {
                    continue;
                }

                errorResponses.Add(new ErrorResponseInfo(item.Key, response));
            }

            errorResponses.Sort(static (left, right) => CompareErrorResponseStatus(left.StatusCodePattern, right.StatusCodePattern));
            return errorResponses;
        }

        private ResponseInfo? ResolveErrorResponse(IOpenApiResponse response)
        {
            if (!TrySelectPreferredContent(response.Content, GetErrorResponseContentPriority, out var selectedContent)
                || !IsUsableErrorContent(selectedContent))
            {
                return null;
            }

            var type = ResolveResponseTypeUsage(selectedContent.Key, selectedContent.Value.Schema);
            var kind = selectedContent.Value.Schema is null && selectedContent.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                ? ResponseKind.String
                : ResolveResponseKind(selectedContent.Key, type);

            return new ResponseInfo(kind, type, !string.IsNullOrWhiteSpace(response.Summary) ? response.Summary : response.Description);
        }

        private TypeUsage ResolveResponseTypeUsage(string contentType, IOpenApiSchema? schema)
        {
            var resolvedType = schema is null
                ? new TypeUsage("string", TypeShape.String, schemaAllowsNull: false, isOptional: false)
                : ResolveTypeUsage(schema, required: true);
            var kind = ResolveResponseKind(contentType, resolvedType);

            if (kind == ResponseKind.Binary)
            {
                return new TypeUsage("byte[]", TypeShape.Binary, resolvedType.SchemaAllowsNull, isOptional: false);
            }

            if (kind == ResponseKind.String)
            {
                return new TypeUsage("string", TypeShape.String, resolvedType.SchemaAllowsNull, isOptional: false);
            }

            return resolvedType;
        }

        private static ResponseKind ResolveResponseKind(string contentType, TypeUsage? type)
        {
            if (type?.Shape == TypeShape.Binary)
            {
                return ResponseKind.Binary;
            }

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return ResponseKind.Json;
            }

            return ResponseKind.String;
        }

        private static IOpenApiResponse? SelectSuccessResponse(OpenApiResponses responses)
        {
            IOpenApiResponse? selectedResponse = null;
            var bestStatusCode = int.MaxValue;
            var selectedHasUsableContent = false;

            foreach (var item in responses)
            {
                if (!IsSuccessResponseStatus(item.Key))
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

            if (selectedResponse is null)
            {
                foreach (var item in responses)
                {
                    if (string.Equals(item.Key, "default", StringComparison.OrdinalIgnoreCase))
                    {
                        return item.Value;
                    }
                }
            }

            return selectedResponse;
        }

        private static int ParseResponseStatusCode(string statusCode)
        {
            return int.TryParse(statusCode, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : int.MaxValue;
        }

        private static bool IsSuccessResponseStatus(string statusCode)
        {
            return statusCode.StartsWith("2", StringComparison.Ordinal);
        }

        private static int CompareErrorResponseStatus(string left, string right)
        {
            return GetErrorResponseStatusSortKey(left).CompareTo(GetErrorResponseStatusSortKey(right));
        }

        private static (int Category, int StatusCode, string Pattern) GetErrorResponseStatusSortKey(string statusCode)
        {
            if (int.TryParse(statusCode, NumberStyles.None, CultureInfo.InvariantCulture, out var exactStatusCode))
            {
                return (0, exactStatusCode, statusCode);
            }

            if (statusCode.Length == 3
                && char.IsDigit(statusCode[0])
                && statusCode[1] is 'X' or 'x'
                && statusCode[2] is 'X' or 'x')
            {
                return (1, (statusCode[0] - '0') * 100, statusCode);
            }

            if (string.Equals(statusCode, "default", StringComparison.OrdinalIgnoreCase))
            {
                return (2, int.MaxValue, statusCode);
            }

            return (3, int.MaxValue, statusCode);
        }

        private static bool HasUsableResponseContent(IOpenApiResponse response)
        {
            if (response.Content is null || response.Content.Count == 0)
            {
                return false;
            }

            foreach (var mediaType in response.Content)
            {
                if (mediaType.Value.Schema is not null || mediaType.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private List<SecuritySchemeBinding> BuildSecuritySchemes()
        {
            var bindings = new List<SecuritySchemeBinding>();
            if (_document.Components?.SecuritySchemes is null)
            {
                return bindings;
            }

            foreach (var scheme in _document.Components.SecuritySchemes)
            {
                var binding = CreateSecuritySchemeBinding(scheme.Key, scheme.Value);
                if (binding is not null)
                {
                    bindings.Add(binding);
                }
            }

            return bindings;
        }

        private IReadOnlyList<SecurityRequirementInfo>? ResolveOperationSecurityRequirements(
            OpenApiOperation operation,
            IReadOnlyList<SecuritySchemeBinding> securitySchemes)
        {
            var requirements = operation.Security ?? _document.Security;
            if (requirements is null)
            {
                return null;
            }

            if (requirements.Count == 0)
            {
                return [];
            }

            var bindingsBySchemeKey = securitySchemes.ToDictionary(static scheme => scheme.SchemeKey, StringComparer.Ordinal);
            var result = new List<SecurityRequirementInfo>(requirements.Count);
            foreach (var requirement in requirements)
            {
                var schemes = new List<SecuritySchemeBinding>();
                foreach (var item in requirement)
                {
                    var schemeKey = item.Key.Reference?.Id ?? item.Key.Name;
                    if (schemeKey is not null && bindingsBySchemeKey.TryGetValue(schemeKey, out var binding))
                    {
                        schemes.Add(binding);
                    }
                }

                if (requirement.Count == 0 || schemes.Count == requirement.Count)
                {
                    result.Add(new SecurityRequirementInfo(schemes));
                }
            }

            return result;
        }

        private static SecuritySchemeBinding? CreateSecuritySchemeBinding(string schemeKey, IOpenApiSecurityScheme scheme)
        {
            if (scheme.Type == SecuritySchemeType.OAuth2
                || (scheme.Type == SecuritySchemeType.Http && string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase)))
            {
                var parameterName = SafeIdentifier(ToCamelCase(schemeKey == "oauth2" ? "access_token" : $"{schemeKey}_token"));
                return new SecuritySchemeBinding(
                    schemeKey,
                    parameterName,
                    $"string? {parameterName} = default",
                    "Authorization",
                    SecuritySchemeLocation.Header,
                    isBearerToken: true);
            }

            if (scheme.Type == SecuritySchemeType.ApiKey)
            {
                var parameterName = SafeIdentifier(ToCamelCase($"{schemeKey}_api_key"));
                return new SecuritySchemeBinding(
                    schemeKey,
                    parameterName,
                    $"string? {parameterName} = default",
                    scheme.Name ?? parameterName,
                    MapLocation(scheme.In ?? OpenApiParameterLocation.Header),
                    isBearerToken: false);
            }

            return null;
        }

        private static SecuritySchemeLocation MapLocation(OpenApiParameterLocation location)
        {
            return location switch
            {
                OpenApiParameterLocation.Query => SecuritySchemeLocation.Query,
                OpenApiParameterLocation.Cookie => SecuritySchemeLocation.Cookie,
                _ => SecuritySchemeLocation.Header,
            };
        }

        private static List<IOpenApiParameter> CollectParameters(IOpenApiPathItem pathItem, OpenApiOperation operation)
        {
            var pathParameters = pathItem.Parameters;
            var operationParameters = operation.Parameters;

            if ((pathParameters is null || pathParameters.Count == 0)
                && (operationParameters is null || operationParameters.Count == 0))
            {
                return [];
            }

            var parameters = new List<IOpenApiParameter>((pathParameters?.Count ?? 0) + (operationParameters?.Count ?? 0));
            var indices = new Dictionary<(OpenApiParameterLocation Location, string Name), int>();
            if (pathParameters is not null)
            {
                foreach (var parameter in pathParameters)
                {
                    AddOrReplaceParameter(parameters, indices, parameter);
                }
            }

            if (operationParameters is not null)
            {
                foreach (var parameter in operationParameters)
                {
                    AddOrReplaceParameter(parameters, indices, parameter);
                }
            }

            return parameters;
        }

        private static void AddOrReplaceParameter(
            List<IOpenApiParameter> parameters,
            Dictionary<(OpenApiParameterLocation Location, string Name), int> indices,
            IOpenApiParameter parameter)
        {
            var location = parameter.In ?? OpenApiParameterLocation.Query;
            if (IsReservedHeaderParameter(location, parameter.Name))
            {
                return;
            }

            var key = (
                location,
                location == OpenApiParameterLocation.Header
                    ? (parameter.Name ?? string.Empty).ToUpperInvariant()
                    : parameter.Name ?? string.Empty);
            if (indices.TryGetValue(key, out var index))
            {
                parameters[index] = parameter;
                return;
            }

            indices.Add(key, parameters.Count);
            parameters.Add(parameter);
        }

        private static int GetRequestBodyContentPriority(KeyValuePair<string, IOpenApiMediaType> item)
        {
            var mediaType = StripMediaTypeParameters(item.Key);
            if (IsJsonMediaType(mediaType))
            {
                return 0;
            }

            if (string.Equals(mediaType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(mediaType, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return int.MaxValue;
        }

        private int GetResponseContentPriority(KeyValuePair<string, IOpenApiMediaType> item)
            => GetContentPriority(item, IsJson, IsBinary, IsText);

        private int GetErrorResponseContentPriority(KeyValuePair<string, IOpenApiMediaType> item)
            => GetContentPriority(item, IsJson, IsText, IsBinary);

        private int GetContentPriority(
            KeyValuePair<string, IOpenApiMediaType> item,
            params Func<KeyValuePair<string, IOpenApiMediaType>, bool>[] predicates)
        {
            for (var i = 0; i < predicates.Length; i++)
            {
                if (predicates[i](item))
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        private static bool IsJson(KeyValuePair<string, IOpenApiMediaType> item)
            => item.Key.Contains("json", StringComparison.OrdinalIgnoreCase);

        private static bool IsText(KeyValuePair<string, IOpenApiMediaType> item)
            => item.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

        private bool IsBinary(KeyValuePair<string, IOpenApiMediaType> item)
            => ResolveResponseTypeUsage(item.Key, item.Value.Schema).Shape == TypeShape.Binary;

        private static bool TrySelectPreferredContent<T>(
            IDictionary<string, T>? content,
            Func<KeyValuePair<string, T>, int> getPriority,
            out KeyValuePair<string, T> selected)
        {
            if (content is null || content.Count == 0)
            {
                selected = default;
                return false;
            }

            using var enumerator = content.GetEnumerator();
            enumerator.MoveNext();
            selected = enumerator.Current;
            var bestPriority = getPriority(selected);

            while (enumerator.MoveNext())
            {
                var candidate = enumerator.Current;
                var priority = getPriority(candidate);
                if (priority < bestPriority)
                {
                    selected = candidate;
                    bestPriority = priority;
                }
            }

            return true;
        }

        private static bool IsUsableErrorContent(KeyValuePair<string, IOpenApiMediaType> content)
        {
            return content.Value.Schema is not null
                || content.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetTagName(OpenApiOperation operation)
        {
            return operation.Tags?.FirstOrDefault()?.Name;
        }

        private static bool IsReservedHeaderParameter(OpenApiParameterLocation location, string? name)
        {
            if (location != OpenApiParameterLocation.Header || string.IsNullOrEmpty(name))
            {
                return false;
            }

            return string.Equals(name, "Accept", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase);
        }

        private static ParameterLocation MapParameterLocation(OpenApiParameterLocation location)
        {
            return location switch
            {
                OpenApiParameterLocation.Path => ParameterLocation.Path,
                OpenApiParameterLocation.Header => ParameterLocation.Header,
                OpenApiParameterLocation.Cookie => ParameterLocation.Cookie,
                _ => ParameterLocation.Query,
            };
        }
    }
}
