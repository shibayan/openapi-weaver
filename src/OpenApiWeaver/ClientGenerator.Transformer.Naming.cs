using System.Globalization;
using System.Text;

using Microsoft.OpenApi;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private static string NormalizePascalIdentifier(string value, string fallback = "Value")
        {
            var normalized = SafeIdentifier(ToPascalCase(value));
            return normalized == "value" && !string.Equals(value, "value", StringComparison.OrdinalIgnoreCase)
                ? SafeIdentifier(ToPascalCase(fallback))
                : normalized;
        }

        private static string NormalizeCamelIdentifier(string value, string fallback = "value")
        {
            var normalized = SafeIdentifier(ToCamelCase(value));
            return normalized == "value" && !string.Equals(value, "value", StringComparison.OrdinalIgnoreCase)
                ? SafeIdentifier(ToCamelCase(fallback))
                : normalized;
        }

        private static string AllocateUniqueName(ISet<string> usedNames, string suggestedName, string fallback = "Value")
        {
            var baseName = string.IsNullOrWhiteSpace(suggestedName) ? fallback : suggestedName;
            var candidate = baseName;
            var suffix = 2;

            while (!usedNames.Add(candidate))
            {
                candidate = baseName + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return candidate;
        }

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

            var normalized = NormalizePascalIdentifier(baseName ?? "OpenApi", "OpenApi");
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

            return NormalizePascalIdentifier(string.Concat(filteredTokens.Select(static token => ToPascalCase(token ?? string.Empty))), "Operation");
        }

        private static string BuildOperationSchemaTypeName(string? operationId, string operationType, string route)
        {
            var source = string.IsNullOrWhiteSpace(operationId) ? $"{operationType}_{route}" : operationId!;
            var tokens = TokenizeWords(source);

            if (tokens.Count == 0)
            {
                tokens = TokenizeWords($"{operationType}_{route}");
            }

            if (tokens.Count == 0)
            {
                tokens.Add("Operation");
            }

            return NormalizePascalIdentifier(string.Concat(tokens.Select(static token => ToPascalCase(token ?? string.Empty))), "Operation");
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
                if (NormalizeToken(segmentTokens[i]) != NormalizeToken(tagTokens[i]))
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
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized.Length == 0)
            {
                return normalized;
            }

            if (normalized is "series" or "species")
            {
                return normalized;
            }

            if (normalized.Length > 4
                && normalized.EndsWith("ies")
                && IsConsonant(normalized[normalized.Length - 4])
                && !HasRepeatedVowelBeforeConsonantIes(normalized))
            {
                return normalized.Substring(0, normalized.Length - 3) + "y";
            }

            if (normalized.Length > 2
                && normalized.EndsWith("s")
                && !normalized.EndsWith("ss")
                && !normalized.EndsWith("us")
                && !normalized.EndsWith("is"))
            {
                if (normalized.EndsWith("ses")
                    || normalized.EndsWith("xes")
                    || normalized.EndsWith("zes")
                    || normalized.EndsWith("ches")
                    || normalized.EndsWith("shes"))
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

        private static bool HasRepeatedVowelBeforeConsonantIes(string value)
        {
            if (value.Length < 7)
            {
                return false;
            }

            var beforeConsonant = value[value.Length - 5];
            var twoBeforeConsonant = value[value.Length - 6];
            return !IsConsonant(beforeConsonant)
                && !IsConsonant(twoBeforeConsonant)
                && beforeConsonant == twoBeforeConsonant;
        }
    }
}
