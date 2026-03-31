# Schema Type Mapping

OpenApiWeaver maps OpenAPI schema types to C# types as follows:

| OpenAPI Type | C# Type |
|---|---|
| `integer` | `int` |
| `integer` (int64) | `long` |
| `number` | `decimal` |
| `number` (float) | `float` |
| `number` (double) | `double` |
| `number` (decimal) | `decimal` |
| `boolean` | `bool` |
| `string` | `string` |
| `string` (date) | `DateOnly` |
| `string` (date-time) | `DateTimeOffset` |
| `string` (uuid) | `Guid` |
| `string` (binary) | `byte[]` |
| `array` | `IReadOnlyList<T>` |
| `object` with `additionalProperties` | `IReadOnlyDictionary<string, T>` |

## Composition keywords

| OpenAPI Keyword | C# Mapping |
|---|---|
| `allOf` | Flattened into a single class |
| `oneOf` / `anyOf` | Union-style nullable properties |

## Enums

OpenAPI `enum` values are mapped to `readonly record struct` types with static members, providing type safety without the limitations of C# `enum`.
