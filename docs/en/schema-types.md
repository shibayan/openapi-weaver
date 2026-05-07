# Schema Type Mapping

OpenApiWeaver maps OpenAPI 3.0-3.2 schema types to C# types according to the following rules.

## Primitive types

| OpenAPI Type | OpenAPI Format | C# Type |
|---|---|---|
| `integer` | - | `int` |
| `integer` | `int64` | `long` |
| `number` | - | `decimal` |
| `number` | `float` | `float` |
| `number` | `double` | `double` |
| `number` | `decimal` | `decimal` |
| `boolean` | - | `bool` |
| `string` | - | `string` |
| `string` | `date` | `DateOnly` |
| `string` | `date-time` | `DateTimeOffset` |
| `string` | `uuid` | `Guid` |
| `string` | `binary` | `byte[]` |

## Collection types

| OpenAPI Schema | C# Type |
|---|---|
| `array` with `items` | `IReadOnlyList<T>` |
| `object` with `additionalProperties` | `IReadOnlyDictionary<string, T>` |
| `object` with `patternProperties` | `IReadOnlyDictionary<string, T>` |

When a top-level component schema defines `additionalProperties` or `patternProperties`, the generated class extends `Dictionary<string, T>` so that it can hold both declared properties and arbitrary key-value pairs:

```yaml
Metadata:
  type: object
  additionalProperties:
    type: string
  properties:
    version:
      type: string
```

```csharp
public sealed class Metadata : Dictionary<string, string>
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }
}
```

When a dictionary-backed schema also declares named properties, OpenApiWeaver emits a custom JSON converter for that type. Known JSON property names populate the generated CLR properties, while additional keys remain in the dictionary. Serialization writes both the declared properties and any extra dictionary entries.

If `additionalProperties` and `patternProperties` produce incompatible value types, the generated type falls back to `Dictionary<string, JsonElement>`.

## Object schemas

Object schemas defined in `components/schemas` are generated as `sealed class` types. Each property receives a `[JsonPropertyName]` attribute that preserves the original JSON name, while the C# property name is converted to PascalCase:

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

Required properties use the `required` modifier, while optional nullable properties are annotated with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`:

```csharp
public sealed class Pet
{
    [JsonPropertyName("id")]
    public required long Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tag")]
    public string? Tag { get; init; }
}
```

## Inline schemas

Inline object or enum schemas defined inside property definitions, array `items`, or `additionalProperties` are generated as nested types under the owning model class, which keeps the type hierarchy compact:

```yaml
Order:
  type: object
  properties:
    status:
      type: string
      enum: [placed, approved, delivered]
```

```csharp
public sealed class Order
{
    [JsonPropertyName("status")]
    public Order.StatusEnum? Status { get; init; }

    public readonly record struct StatusEnum(string Value) { ... }
}
```

## Composition keywords

| OpenAPI Keyword | C# Mapping |
|---|---|
| `allOf` | Flattened into a single class containing all properties |
| `oneOf` / `anyOf` | Union-style nullable properties or nullable CLR primitives when the union is a primitive plus `null` |
| `discriminator` + `oneOf` | Polymorphic base class annotated with `JsonPolymorphic` / `JsonDerivedType`, with referenced schemas emitted as derived classes |

### Discriminator-based polymorphism

When a component schema declares `discriminator` and `oneOf` references named component schemas, OpenApiWeaver treats that schema as the polymorphic base type.

- The base type is emitted as `public class` instead of `public sealed class`
- The base type receives `[JsonPolymorphic(TypeDiscriminatorPropertyName = "...")]`
- Each `oneOf` member produces a `[JsonDerivedType(typeof(...), typeDiscriminator: "...")]` attribute
- Referenced child schemas are emitted as derived classes
- `discriminator.mapping` keys are used as discriminator values when present; otherwise the schema names are used

To keep System.Text.Json polymorphic serialization consistent, the discriminator property is not emitted as a normal CLR property on the generated types.

This feature currently requires all discriminator members to be `$ref` entries in `oneOf`. Using `anyOf`, inline `oneOf` members, duplicate discriminator values, or mappings that point outside `oneOf` is rejected with `OAW004`.

## Nullable type arrays (OpenAPI 3.2)

OpenAPI 3.2 allows `type` to be expressed as an array. When one of the entries is `null`, OpenApiWeaver maps the schema to a nullable CLR type.

```yaml
partnerResponse:
  type: object
  properties:
    display_name:
      type: [string, 'null']
    company_id:
      type: [integer, 'null']
```

```csharp
public sealed class PartnerResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("company_id")]
    public int? CompanyId { get; init; }
}
```

## Enums

### String enums

OpenAPI string `enum` values are generated as `readonly record struct` types with static members. This approach provides type safety and IntelliSense without the limitations of C# `enum`, which supports only numeric values:

```yaml
# OpenAPI schema
Status:
  type: string
  enum: [active, inactive, pending]
```

```csharp
// Generated C#
[JsonConverter(typeof(StatusJsonConverter))]
public readonly record struct Status(string Value)
{
    public static readonly Status Active = new("active");
    public static readonly Status Inactive = new("inactive");
    public static readonly Status Pending = new("pending");

    public override string ToString() => Value;
}

public sealed class StatusJsonConverter : JsonConverter<Status>
{
    public override Status Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new Status(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, Status value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
```

A dedicated `JsonConverter` is generated for each string enum so that serialization and deserialization follow the schema definition.

### Integer enums

When the OpenAPI schema specifies `type: integer` together with `enum`, a standard C# `enum` is generated instead:

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
    Value2 = 2
}
```

When the schema specifies `format: int64`, the generated enum uses `long` as the underlying type:

```csharp
public enum Priority : long
{
    Value0 = 0,
    Value1 = 1,
    Value2 = 2
}
```

## Naming conventions

All schema names are converted from their original casing to idiomatic C# naming conventions:

| Source Convention | C# Convention | Example |
|---|---|---|
| `snake_case` | `PascalCase` | `pet_name` -> `PetName` |
| `kebab-case` | `PascalCase` | `pet-name` -> `PetName` |
| `camelCase` | `PascalCase` | `petName` -> `PetName` |

The original names are always preserved in `[JsonPropertyName]` attributes to ensure correct serialization.
