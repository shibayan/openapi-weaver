# Schema Type Mapping

OpenApiWeaver maps OpenAPI schema types to C# types as follows.

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

When a top-level component schema defines `additionalProperties` or `patternProperties`, the generated class **extends** `Dictionary<string, T>` so that it can hold both declared properties and arbitrary key-value pairs:

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

Inline object or enum schemas defined inside property definitions, array `items`, or `additionalProperties` are generated as **nested types** under the owning model class, keeping the type hierarchy compact:

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

A dedicated `JsonConverter` is generated for each string enum to handle serialization and deserialization correctly.

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

All schema names are converted from their original casing to C# idiomatic conventions:

| Source Convention | C# Convention | Example |
|---|---|---|
| `snake_case` | `PascalCase` | `pet_name` -> `PetName` |
| `kebab-case` | `PascalCase` | `pet-name` -> `PetName` |
| `camelCase` | `PascalCase` | `petName` -> `PetName` |

The original names are always preserved in `[JsonPropertyName]` attributes to ensure correct serialization.
