# Contributing to OpenApiWeaver

Thank you for contributing to OpenApiWeaver.

## Before You Start

- Read the project overview in `README.md`.
- Search existing issues and pull requests before starting new work.
- For security issues, do not open a public issue. Follow `SECURITY.md` instead.

## Development Environment

- .NET SDK 10.0 or later
- A local clone of this repository

## Build and Test

Run these commands from the repository root:

```bash
dotnet build OpenApiWeaver.slnx
dotnet test OpenApiWeaver.slnx
```

If you want to inspect generated output manually:

```bash
dotnet run --project samples/SampleApp/SampleApp.csproj
```

## Making Changes

- Keep changes focused on a single problem.
- Preserve the current public behavior unless the change explicitly intends to alter it.
- Add or update tests for generator output, diagnostics, or supported OpenAPI shapes when behavior changes.
- Update `README.md` when user-facing behavior, configuration, or limitations change.
- Update `AnalyzerReleases.Unshipped.md` when introducing or changing diagnostics.

## Pull Requests

- Open pull requests against `master`.
- Include a short description of the problem and the approach used.
- Link the related issue when one exists.
- Make sure build and tests pass before requesting review.
- Call out breaking changes and notable generator output changes clearly in the description.

## Review Expectations

- Pull requests may receive feedback on API shape, generated code quality, naming, diagnostics, and compatibility.
- Incomplete experiments are better discussed in an issue before a large pull request is opened.

## Questions

Use a GitHub issue for questions about usage, supported scenarios, or proposed changes.
