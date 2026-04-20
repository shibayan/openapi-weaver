using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private static GeneratorInput? TryCreateInput(AdditionalText file, AnalyzerConfigOptionsProvider optionsProvider, CancellationToken cancellationToken)
    {
        var fileOptions = optionsProvider.GetOptions(file);
        if (!fileOptions.TryGetValue(BuildMetadataAdditionalFilesItemKind, out var itemKind)
            || !string.Equals(itemKind, OpenApiWeaverDocumentItemKind, StringComparison.Ordinal))
        {
            return null;
        }

        optionsProvider.GlobalOptions.TryGetValue(BuildPropertyRootNamespace, out var rootNamespace);
        fileOptions.TryGetValue(BuildMetadataAdditionalFilesNamespace, out var configuredNamespace);
        fileOptions.TryGetValue(BuildMetadataAdditionalFilesClientName, out var clientName);

        var content = file.GetText(cancellationToken)?.ToString() ?? string.Empty;

        var effectiveNamespace = NormalizeOption(configuredNamespace) ?? rootNamespace ?? string.Empty;

        return new GeneratorInput(
            file.Path,
            content,
            effectiveNamespace,
            NormalizeOption(clientName));
    }

    private static string? NormalizeOption(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private const string BuildPropertyRootNamespace = "build_property.RootNamespace";
    private const string BuildMetadataAdditionalFilesClientName = "build_metadata.AdditionalFiles.ClientName";
    private const string BuildMetadataAdditionalFilesNamespace = "build_metadata.AdditionalFiles.Namespace";
    private const string BuildMetadataAdditionalFilesItemKind = "build_metadata.AdditionalFiles.OpenApiWeaverItemKind";
    private const string OpenApiWeaverDocumentItemKind = "Document";

    private sealed class GeneratorInput(string path, string content, string rootNamespace, string? clientName) : IEquatable<GeneratorInput>
    {
        public string Path { get; } = path;

        public string Content { get; } = content;

        public string RootNamespace { get; } = rootNamespace;

        public string? ClientName { get; } = clientName;

        public bool Equals(GeneratorInput? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return string.Equals(Path, other.Path, StringComparison.Ordinal)
                && string.Equals(Content, other.Content, StringComparison.Ordinal)
                && string.Equals(RootNamespace, other.RootNamespace, StringComparison.Ordinal)
                && string.Equals(ClientName, other.ClientName, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as GeneratorInput);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Path.GetHashCode();
                hash = (hash * 397) ^ Content.GetHashCode();
                hash = (hash * 397) ^ RootNamespace.GetHashCode();
                hash = (hash * 397) ^ (ClientName?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
