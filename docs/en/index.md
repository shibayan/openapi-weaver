---
layout: home

hero:
  name: OpenApiWeaver
  text: OpenAPI to C# Client Generator
  tagline: An incremental Roslyn source generator that turns OpenAPI 3.x documents into strongly typed C# HTTP clients at build time.
  actions:
    - theme: brand
      text: Get Started
      link: /getting-started
    - theme: alt
      text: View on GitHub
      link: https://github.com/shibayan/openapi-weaver

features:
  - title: Zero Runtime Overhead
    details: All code is generated at compile time via Roslyn source generators - no runtime code generation, no reflection, no additional runtime dependencies.
  - title: Incremental & Fast
    details: Leverages the Roslyn incremental generator pipeline so only changed documents are re-processed, keeping rebuilds fast even in large solutions.
  - title: Just Add the NuGet Package
    details: The package bundles the source generator and all required analyzer assemblies. No CLI tools, no extra MSBuild steps.
  - title: Strongly Typed with IntelliSense
    details: Full IntelliSense, compile-time type safety, and navigation support for all generated clients, operations, and DTOs.
  - title: JSON & YAML
    details: Reads OpenAPI 3.x documents in JSON (.json) and YAML (.yaml, .yml) formats.
  - title: Build-Time Diagnostics
    details: Reports errors and warnings for invalid or unsupported OpenAPI documents as standard compiler diagnostics.
---