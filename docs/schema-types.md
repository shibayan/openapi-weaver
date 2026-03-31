# Schema Type Mapping

OpenApiWeaver maps OpenAPI schema types to C# types as follows.

## Primitive types

| OpenAPI Type | OpenAPI Format | C# Type |
|---|---|---|
| `integer` | — | `int` |
| `integer` | `int64` | `long` |
| `number` | — | `decimal` |
| `number` | `float` | `float` |
| `number` | `double` | `double` |
| `number` | `decimal` | `decimal` |
| `boolean` | — | `bool` |
| `string` | — | `string` |
| `string` | `date` | `DateOnly` |
| `string` | `date-time` | `DateTimeOffset` |
| `string` | `uuid` | `Guid` |
| `string` | `binary` | `byte[]` |

## Collection types

| OpenAPI Schema | C# Type |
|---|---|
| `array` with `items` | `IReadOnlyList<T>` |
| `object` with `additionalProperties` | `IReadOnlyDictionary<string, T>` |

## Object schemas

Object schemas defined in `components/schemas` are generated as `sealed class` types. Each property gets a `[JsonPropertyName]` attribute preserving the original JSON name, while the C# property name is converted to PascalCase:

```yaml
# OpenAPI schema
Pet:
  type: object
  required: [id, name]
  properties:
    id:
      type: integer
      format: int64
    pet_name:
      type: string
```

```csharp
// Generated C#
public sealed class Pet
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonPropertyName("pet_name")]
    public required string PetName { get; init; }
}
```

## Composition keywords

| OpenAPI Keyword | C# Mapping |
|---|---|
| `allOf` | Flattened into a single class containing all properties |
| `oneOf` / `anyOf` | Union-style nullable properties |

## Enums

### String enums

OpenAPI string `enum` values are generated as `readonly record struct` types with static members. This approach provides type safety and IntelliSense without the limitations of C# `enum` (which only supports numeric values):

```yaml
# OpenAPI schema
Status:
  type: string
  enum: [active, inactive, pending]
```

```csharp
// Generated C#
public readonly record struct Status(string Value)
{
    public static readonly Status Active = new("active");
    public static readonly Status Inactive = new("inactive");
    public static readonly Status Pending = new("pending");
}
```

### Integer enums

When the OpenAPI schema specifies `type: integer` with `enum`, a standard C# `enum` is generated instead:

```yaml
# OpenAPI schema
Priority:
  type: integer
  enum: [0, 1, 2]
```

```csharp
// Generated C#
public enum Priority
{
    Value0 = 0,
    Value1 = 1,
    Value2 = 2,
}
```

## Naming conventions

All schema names are converted from their original casing to C# idiomatic conventions:

| Source Convention | C# Convention | Example |
|---|---|---|
| `snake_case` | `PascalCase` | `pet_name` → `PetName` |
| `kebab-case` | `PascalCase` | `pet-name` → `PetName` |
| `camelCase` | `PascalCase` | `petName` → `PetName` |

The original names are always preserved in `[JsonPropertyName]` attributes to ensure correct serialization.
