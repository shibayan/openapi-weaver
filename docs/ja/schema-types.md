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
public readonly record struct Status(string Value)
{
    public static readonly Status Active = new("active");
    public static readonly Status Inactive = new("inactive");
    public static readonly Status Pending = new("pending");
}
```

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
    Value2 = 2,
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
