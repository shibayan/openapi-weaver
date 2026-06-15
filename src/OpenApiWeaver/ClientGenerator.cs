using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.OpenApi.Reader;

namespace OpenApiWeaver;

[Generator]
public sealed partial class ClientGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var openApiFiles = context.AdditionalTextsProvider
            .Where(static file => IsOpenApiDocument(file.Path))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) => TryCreateInput(pair.Left, pair.Right, cancellationToken))
            .Where(static input => input is not null);

        context.RegisterSourceOutput(openApiFiles, static (productionContext, input) =>
        {
            EmitClientSource(productionContext, input!);
        });

        var namespaces = openApiFiles
            .Select(static (input, _) => input!.RootNamespace)
            .Collect()
            .SelectMany(static (items, _) => items.Distinct(StringComparer.Ordinal));

        context.RegisterSourceOutput(namespaces, static (productionContext, rootNamespace) =>
        {
            var source = SupportTypesEmitter.Emit(rootNamespace);
            var hintName = string.IsNullOrEmpty(rootNamespace)
                ? "OpenApiWeaver.Support.g.cs"
                : $"{SanitizeHintName(rootNamespace)}.Support.g.cs";
            productionContext.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        });
    }

    private static void EmitClientSource(SourceProductionContext productionContext, GeneratorInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Content))
        {
            productionContext.ReportDiagnostic(OpenApiWeaverDiagnostics.CreateDocumentEmpty(input.Path));
            return;
        }

        var readResult = default(ReadResult);
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input.Content), writable: false);
            readResult = ReadOpenApiDocument(input.Path, stream);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            productionContext.ReportDiagnostic(OpenApiWeaverDiagnostics.CreateDocumentInvalid(input.Path, exception.Message));
            return;
        }

        var document = readResult.Document;
        var diagnostic = readResult.Diagnostic;
        if (document?.Paths is null)
        {
            productionContext.ReportDiagnostic(OpenApiWeaverDiagnostics.CreateDocumentInvalid(
                input.Path,
                "The document does not contain valid paths or could not be loaded."));
            return;
        }

        if (diagnostic?.Errors.Count > 0)
        {
            productionContext.ReportDiagnostic(OpenApiWeaverDiagnostics.CreateDocumentHasWarnings(
                input.Path,
                diagnostic.Errors.Select(static error => error.Message)));
        }

        try
        {
            var model = new Transformer(input.Path, input.RootNamespace, input.ClientName, document).Transform();
            var source = new Emitter(model).Emit();
            productionContext.AddSource($"{SanitizeHintName(input.Path)}.g.cs", SourceText.From(source, Encoding.UTF8));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnsupportedGenerationException exception)
        {
            productionContext.ReportDiagnostic(OpenApiWeaverDiagnostics.CreateDocumentUnsupported(input.Path, exception.Message));
        }
        catch (Exception exception)
        {
            productionContext.ReportDiagnostic(OpenApiWeaverDiagnostics.CreateDocumentInvalid(input.Path, exception.Message));
        }
    }

    private static string SanitizeHintName(string value)
        => value.Replace('<', '_').Replace('>', '_').Replace('.', '_').Replace('\\', '_').Replace('/', '_').Replace(':', '_');

    private sealed class UnsupportedGenerationException(string message) : Exception(message);
}
