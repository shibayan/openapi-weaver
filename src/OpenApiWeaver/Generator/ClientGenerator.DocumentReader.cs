using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private static bool IsOpenApiDocument(string path)
    {
        var extension = Path.GetExtension(path);
        return IsYamlExtension(extension)
            || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static ReadResult ReadOpenApiDocument(string path, MemoryStream stream)
    {
        var location = CreateDocumentUri(path);
        var settings = new OpenApiReaderSettings();

        if (IsYamlExtension(Path.GetExtension(path)))
        {
            settings.AddYamlReader();
            return new OpenApiYamlReader().Read(stream, location, settings);
        }

        settings.AddJsonReader();
        return new OpenApiJsonReader().Read(stream, location, settings);
    }

    private static bool IsYamlExtension(string extension)
    {
        return string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri CreateDocumentUri(string path)
    {
        return Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(Path.GetFullPath(path));
    }
}
