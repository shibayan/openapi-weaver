using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private static int GetRequestBodyContentPriority(KeyValuePair<string, IOpenApiMediaType> item)
            => TryResolveRequestBodyKind(item.Key) is { } kind ? (int)kind : int.MaxValue;

        private int GetResponseContentPriority(KeyValuePair<string, IOpenApiMediaType> item)
            => IsJson(item) ? 0 : IsBinary(item) ? 1 : IsText(item) ? 2 : int.MaxValue;

        private int GetErrorResponseContentPriority(KeyValuePair<string, IOpenApiMediaType> item)
            => IsJson(item) ? 0 : IsText(item) ? 1 : IsBinary(item) ? 2 : int.MaxValue;

        private static bool IsJson(KeyValuePair<string, IOpenApiMediaType> item)
            => IsJsonMediaType(StripMediaTypeParameters(item.Key));

        private static bool IsJsonMediaType(string mediaType)
        {
            if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                && mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        }

        private static string StripMediaTypeParameters(string contentType)
        {
            var separatorIndex = contentType.IndexOf(';');
            return separatorIndex < 0 ? contentType.Trim() : contentType.Substring(0, separatorIndex).Trim();
        }

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

        private static bool IsUsableContent(KeyValuePair<string, IOpenApiMediaType> content)
        {
            return content.Value.Schema is not null
                || content.Key.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
