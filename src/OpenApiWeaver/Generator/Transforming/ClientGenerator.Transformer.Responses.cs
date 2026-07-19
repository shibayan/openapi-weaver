using System.Globalization;

using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private ResponseInfo ResolveResponse(OpenApiOperation operation)
        {
            if (!TrySelectSuccessResponseContent(operation, out var response, out var selectedContent))
            {
                return new ResponseInfo(ResponseKind.None, type: null, null);
            }

            var type = ResolveResponseTypeUsage(selectedContent.Key, selectedContent.Value.Schema);
            var kind = ResolveResponseKind(selectedContent.Key, type);

            return new ResponseInfo(kind, type, !string.IsNullOrWhiteSpace(response.Summary) ? response.Summary : response.Description);
        }

        private bool TrySelectSuccessResponseContent(
            OpenApiOperation operation,
            out IOpenApiResponse response,
            out KeyValuePair<string, IOpenApiMediaType> selectedContent)
        {
            var selected = SelectSuccessResponse(operation.Responses ?? []);
            if (selected is not null && TrySelectPreferredContent(selected.Content, GetResponseContentPriority, out selectedContent))
            {
                response = selected;
                return true;
            }

            response = null!;
            selectedContent = default;
            return false;
        }

        private List<ErrorResponseInfo> ResolveErrorResponses(OpenApiOperation operation)
        {
            var errorResponses = new List<ErrorResponseInfo>();
            foreach (var (statusPattern, response, selectedContent) in EnumerateErrorResponseContents(operation))
            {
                errorResponses.Add(new ErrorResponseInfo(statusPattern, CreateErrorResponseInfo(response, selectedContent)));
            }

            errorResponses.Sort(static (left, right) => CompareErrorResponseStatus(left.StatusCodePattern, right.StatusCodePattern));
            return errorResponses;
        }

        private IEnumerable<(string StatusPattern, IOpenApiResponse Response, KeyValuePair<string, IOpenApiMediaType> Content)> EnumerateErrorResponseContents(OpenApiOperation operation)
        {
            if (operation.Responses is null || operation.Responses.Count == 0)
            {
                yield break;
            }

            var hasSuccessStatus = operation.Responses.Any(static item => IsSuccessResponseStatus(item.Key));

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

                if (!TrySelectPreferredContent(item.Value.Content, GetErrorResponseContentPriority, out var selectedContent)
                    || !IsUsableContent(selectedContent))
                {
                    continue;
                }

                yield return (item.Key, item.Value, selectedContent);
            }
        }

        private ResponseInfo CreateErrorResponseInfo(IOpenApiResponse response, KeyValuePair<string, IOpenApiMediaType> selectedContent)
        {
            var type = ResolveResponseTypeUsage(selectedContent.Key, selectedContent.Value.Schema);
            var kind = selectedContent.Value.Schema is null && selectedContent.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                ? ResponseKind.String
                : ResolveResponseKind(selectedContent.Key, type);

            return new ResponseInfo(kind, type, !string.IsNullOrWhiteSpace(response.Summary) ? response.Summary : response.Description);
        }

        private TypeUsage ResolveResponseTypeUsage(string contentType, IOpenApiSchema? schema)
        {
            var resolvedType = schema is null
                ? TypeUsage.Create("string", TypeShape.String, schemaAllowsNull: false, isOptional: false)
                : _schemaTypeResolver.ResolveTypeUsage(schema, required: true);
            var kind = ResolveResponseKind(contentType, resolvedType);

            if (kind == ResponseKind.Binary)
            {
                return TypeUsage.Create("byte[]", TypeShape.Binary, resolvedType.SchemaAllowsNull, isOptional: false);
            }

            if (kind == ResponseKind.String)
            {
                return TypeUsage.Create("string", TypeShape.String, resolvedType.SchemaAllowsNull, isOptional: false);
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
                if (IsUsableContent(mediaType))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
