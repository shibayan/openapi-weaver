# openapi-roslyn

`openapi-roslyn` is an incremental Roslyn source generator that turns OpenAPI documents into strongly typed C# HTTP clients at build time.

The generator reads OpenAPI `.json`, `.yaml`, and `.yml` files added as `AdditionalFiles`, parses them with `Microsoft.OpenApi`, and emits:

- A top-level client class for the document
- Tag-based sub clients for grouped operations
- Request and response DTOs from component schemas
- Serialization metadata such as `JsonPropertyName` for snake_case fields

## Features

- Incremental source generation via Roslyn
- Supports OpenAPI documents in JSON and YAML
- Generates client classes grouped by OpenAPI tags
- Generates typed request and response models from schemas
- Handles JSON, `application/x-www-form-urlencoded`, and `multipart/form-data` request bodies
- Maps text responses to `string`, binary responses to `byte[]`, and empty success responses to `Task`
- Initializes authentication headers from OpenAPI security schemes when possible
- Reports diagnostics for empty, invalid, and warning-producing OpenAPI documents

## Repository Layout

- `src/OpenApiClientGenerator`: source generator implementation
- `tests/OpenApiClientGenerator.Tests`: xUnit tests for generated output behavior
- `samples/SampleApp`: minimal sample project that consumes the generator

## Requirements

- .NET SDK with support for `net10.0` for the sample app and tests
- A C# project that can reference Roslyn analyzers/source generators

The generator itself targets `netstandard2.0`.

## How It Works

For each OpenAPI document included as an additional file, the generator creates a client whose name is based on the file name.

Example:

- `api-schema.json` -> `ApiSchemaClient`
- `petstore.yaml` -> `PetstoreClient`

Operations are grouped by OpenAPI tags. If a document has a `Tags` tag, the generated root client exposes a `Tags` property whose type is a generated tag client.

## Using the Generator in a Project

The sample project shows the current development-time setup. Add the generator project as an analyzer and include your OpenAPI document as an additional file.

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\OpenApiClientGenerator\OpenApiClientGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />

  <Analyzer Include="..\..\src\OpenApiClientGenerator\bin\Debug\netstandard2.0\Microsoft.OpenApi.dll" />
  <Analyzer Include="..\..\src\OpenApiClientGenerator\bin\Debug\netstandard2.0\Microsoft.OpenApi.YamlReader.dll" />
</ItemGroup>

<ItemGroup>
  <AdditionalFiles Include="openapi\api-schema.json" />
</ItemGroup>
```

Build the generator project before building the consuming project so the analyzer assembly and its dependencies exist under `bin/Debug/netstandard2.0`.

## Minimal Example

If your project includes `openapi/api-schema.json`, the generated client name becomes `ApiSchemaClient`.

```csharp
using SampleApp;

using var client = new ApiSchemaClient(accessToken: "dummy-access-token");

Console.WriteLine(client.Tags.GetType().FullName);
```

The generated root client:

- Creates an internal `HttpClient`
- Sets `BaseAddress` from the first OpenAPI server URL when available
- Applies security settings such as bearer tokens from constructor arguments
- Exposes one property per tag group

Generated operation methods are based on `operationId` when present.

## Generated Behavior

Current test coverage verifies the following behavior:

- Empty documents report an error diagnostic and generate no source
- Invalid documents report an error diagnostic and generate no source
- Multipart request bodies generate `byte[]` properties for binary fields and multipart content helpers
- Optional form bodies generate nullable method parameters and conditional request content assignment
- No-content success responses generate non-generic `Task`
- Binary responses generate `Task<byte[]>`
- Snake_case schema properties are preserved with `JsonPropertyName`
- Mixed JSON and form request bodies prefer JSON when both are available
- Plain text responses generate `Task<string>`

## Build

```bash
dotnet build OpenApiRoslyn.slnx
```

## Test

```bash
dotnet test OpenApiRoslyn.slnx
```

## Sample App

```bash
dotnet run --project samples/SampleApp/SampleApp.csproj
```

The sample project enables emitted generated files under `samples/SampleApp/obj/Generated` so you can inspect the generated client code during development.

## Current Status

This repository is currently structured as a source generator project plus a sample consumer project. The recommended integration path in this repository is project-to-project analyzer referencing during development.
