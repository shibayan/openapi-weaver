# スキーマ型マッピング

OpenApiWeaver は OpenAPI のスキーマ型を次のように C# 型へマッピングします。

## プリミティブ型

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

## コレクション型

| OpenAPI Schema | C# Type |
|---|---|
| `array` with `items` | `IReadOnlyList<T>` |
| `object` with `additionalProperties` | `IReadOnlyDictionary<string, T>` |
| `object` with `patternProperties` | `IReadOnlyDictionary<string, T>` |

トップレベルのコンポーネントスキーマで `additionalProperties` または `patternProperties` が定義されている場合、生成クラスは `Dictionary<string, T>` を **継承** し、宣言されたプロパティと任意のキーバリューペアの両方を保持できます。

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

## オブジェクトスキーマ

`components/schemas` に定義されたオブジェクトスキーマは `sealed class` として生成されます。各プロパティには元の JSON 名を保持する `[JsonPropertyName]` 属性が付与され、C# 側のプロパティ名は PascalCase に変換されます。

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

必須プロパティには `required` 修飾子が付与され、任意かつ nullable なプロパティには `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` が付与されます。

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

## インラインスキーマ

プロパティ定義や配列の `items`、`additionalProperties` 内に定義されたインラインのオブジェクト / enum スキーマは、親モデルクラスの **ネスト型** として生成され、型階層がコンパクトに保たれます。

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

## 合成キーワード

| OpenAPI Keyword | C# Mapping |
|---|---|
| `allOf` | すべてのプロパティを含む 1 つのクラスへフラット化 |
| `oneOf` / `anyOf` | union 風の nullable プロパティ |

## 列挙型

### 文字列 enum

OpenAPI の文字列 `enum` 値は、static メンバー付きの `readonly record struct` として生成されます。この方式により、数値しか扱えない C# `enum` の制約を受けずに、型安全性と IntelliSense を両立できます。

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

各文字列 enum に対して、シリアライズとデシリアライズを正しく処理するための専用 `JsonConverter` が生成されます。

### 整数 enum

OpenAPI スキーマで `type: integer` と `enum` が指定されている場合は、通常の C# `enum` が生成されます。

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

スキーマで `format: int64` が指定されている場合、生成される enum は基底型として `long` を使用します。

```csharp
public enum Priority : long
{
    Value0 = 0,
    Value1 = 1,
    Value2 = 2
}
```

## 命名規則

すべてのスキーマ名は、元の表記から C# で一般的な命名規則へ変換されます。

| Source Convention | C# Convention | Example |
|---|---|---|
| `snake_case` | `PascalCase` | `pet_name` → `PetName` |
| `kebab-case` | `PascalCase` | `pet-name` → `PetName` |
| `camelCase` | `PascalCase` | `petName` → `PetName` |

シリアライズの正確性を保つため、元の名前は常に `[JsonPropertyName]` 属性に保持されます。
