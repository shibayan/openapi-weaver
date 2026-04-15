---
layout: home

hero:
  name: OpenApiWeaver
  text: Build-time OpenAPI to C# Client Generation
  tagline: Incremental Roslyn source generation for strongly typed C# HTTP clients from OpenAPI 3.x documents, including OpenAPI 3.2.
  image:
    src: /icon.svg
    alt: OpenApiWeaver
  actions:
    - theme: brand
      text: Get Started
      link: /getting-started
    - theme: alt
      text: View on GitHub
      link: https://github.com/shibayan/openapi-weaver

features:
  - title: Zero Runtime Overhead
    details: Generates all client code at compile time through Roslyn source generators, without runtime code generation, reflection, or additional runtime dependencies.
  - title: Incremental & Fast
    details: Uses the Roslyn incremental generator pipeline so only changed documents are reprocessed, which keeps rebuilds efficient even in large solutions.
  - title: NuGet Package Distribution
    details: Ships as a single NuGet package that includes the source generator and required analyzer assemblies, without separate CLI tools or additional MSBuild steps.
  - title: Strongly Typed with IntelliSense
    details: Provides IntelliSense, compile-time type safety, and navigation support for generated clients, operations, and DTOs.
  - title: JSON & YAML
    details: Reads OpenAPI 3.0-3.2 documents in JSON (.json) and YAML (.yaml, .yml) formats.
  - title: Build-Time Diagnostics
    details: Reports invalid or unsupported OpenAPI documents as standard compiler warnings and errors.
---
