using System.Text;

using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class DocumentTransformer
    {
        private static string BuildClientName(string documentPath, OpenApiDocument document, string? configuredClientName)
        {
            var baseName = configuredClientName;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = Path.GetFileNameWithoutExtension(documentPath);
            }

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = !string.IsNullOrWhiteSpace(document.Info.Title)
                    ? document.Info.Title
                    : "OpenApi";
            }

            var normalized = SafeIdentifier(ToPascalCase(baseName ?? "OpenApi"));
            return normalized.EndsWith("Client", StringComparison.Ordinal) ? normalized : $"{normalized}Client";
        }

        private static string BuildOperationMethodName(string? operationId, string operationType, string route, string? tagName)
        {
            var source = operationId ?? $"{operationType}_{route}";
            var operationTokens = TokenizeWords(source);
            var tagTokens = new HashSet<string>(
                TokenizeWords(tagName ?? string.Empty)
                    .Select(NormalizeToken)
                    .Where(static token => token.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            var filteredTokens = operationTokens
                .Where(token => !tagTokens.Contains(NormalizeToken(token)))
                .ToList();

            if (filteredTokens.Count == 0)
            {
                filteredTokens.Add(operationType.ToLowerInvariant());
            }

            if (string.Equals(operationType, "get", StringComparison.OrdinalIgnoreCase)
                && TryBuildCanonicalGetMethodName(route, tagName, filteredTokens) is { } canonicalGetName)
            {
                return canonicalGetName;
            }

            return SafeIdentifier(string.Concat(filteredTokens.Select(static token => ToPascalCase(token ?? string.Empty))));
        }

        private static string? TryBuildCanonicalGetMethodName(string route, string? tagName, IReadOnlyList<string> filteredTokens)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            var normalizedTag = NormalizeToken(tagName!);
            if (normalizedTag.Length == 0 || !IsSelfReferentialGetName(filteredTokens, normalizedTag))
            {
                return null;
            }

            var segments = route.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            var lastSegment = segments[segments.Length - 1];
            if (!IsPathParameterSegment(lastSegment)
                && SegmentMatchesTag(lastSegment, tagName!))
            {
                return "List";
            }

            if (segments.Length >= 2
                && IsPathParameterSegment(lastSegment)
                && SegmentMatchesTag(segments[segments.Length - 2], tagName!))
            {
                return "Get";
            }

            return null;
        }

        private static bool SegmentMatchesTag(string segment, string tagName)
        {
            var segmentTokens = TokenizeWords(segment);
            var tagTokens = TokenizeWords(tagName);

            if (segmentTokens.Count != tagTokens.Count)
            {
                return false;
            }

            for (var i = 0; i < segmentTokens.Count; i++)
            {
                if (!string.Equals(NormalizeToken(segmentTokens[i]), NormalizeToken(tagTokens[i]), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSelfReferentialGetName(IReadOnlyList<string> filteredTokens, string normalizedTag)
        {
            if (filteredTokens.Count == 0)
            {
                return true;
            }

            var index = 0;
            if (IsCanonicalGetVerb(filteredTokens[0]))
            {
                index++;
            }

            if (index >= filteredTokens.Count)
            {
                return true;
            }

            return filteredTokens.Skip(index).All(token => token is not null && NormalizeToken(token) == normalizedTag);
        }

        private static bool IsCanonicalGetVerb(string token)
        {
            var normalized = NormalizeToken(token);
            return normalized is "get" or "list";
        }

        private static bool IsPathParameterSegment(string segment)
        {
            return segment.Length > 2
                && segment[0] == '{'
                && segment[segment.Length - 1] == '}';
        }

        private static List<string> TokenizeWords(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return [];
            }

            var normalized = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                normalized.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }

            return [.. normalized.ToString().Split([' '], StringSplitOptions.RemoveEmptyEntries)];
        }

        private static string NormalizeToken(string value)
        {
            var normalized = value.Trim();
            if (normalized.Length == 0)
            {
                return normalized;
            }

            // Invariant words (same in singular and plural)
            if (string.Equals(normalized, "series", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "species", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            // consonant + "ies" → consonant + "y" (e.g., "companies" → "company")
            // Vowel + "ies" falls through to simple "s" removal (e.g., "movies" → "movie")
            if (normalized.Length > 3
                && normalized.EndsWith("ies", StringComparison.OrdinalIgnoreCase)
                && IsConsonant(normalized[normalized.Length - 4]))
            {
                return normalized.Substring(0, normalized.Length - 3) + "y";
            }

            if (normalized.Length > 2
                && normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith("ss", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith("us", StringComparison.OrdinalIgnoreCase)
                && !normalized.EndsWith("is", StringComparison.OrdinalIgnoreCase))
            {
                if (normalized.EndsWith("ses", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith("xes", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith("zes", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith("ches", StringComparison.OrdinalIgnoreCase)
                    || normalized.EndsWith("shes", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(0, normalized.Length - 2);
                }
                else
                {
                    normalized = normalized.Substring(0, normalized.Length - 1);
                }
            }

            return normalized;
        }

        private static bool IsConsonant(char ch)
        {
            var lower = char.ToLowerInvariant(ch);
            return char.IsLetter(lower) && lower is not 'a' and not 'e' and not 'i' and not 'o' and not 'u';
        }
    }
}
