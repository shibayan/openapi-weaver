using Microsoft.CodeAnalysis;

using Xunit;

namespace OpenApiClientGenerator.Tests;

public sealed partial class SourceGeneratorRequestResponseTests
{
    [Fact]
    public void EmptyDocument_ReportsDiagnostic_AndDoesNotGenerateSource()
    {
        var result = RunGenerator("   ");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OARSG002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void InvalidDocument_ReportsDiagnostic_AndDoesNotGenerateSource()
    {
        var result = RunGenerator("not: [valid");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("OARSG004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedSources);
    }
}
