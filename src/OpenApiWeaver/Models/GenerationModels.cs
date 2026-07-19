namespace OpenApiWeaver.Models;

internal sealed class ClientModel(
    string rootNamespace,
    string clientName,
    string serializerOptionsTypeName,
    string summary,
    string? description,
    string? serverUrl,
    IReadOnlyList<SchemaDefinition> schemas,
    IReadOnlyList<TagGroup> tagGroups,
    IReadOnlyList<SecuritySchemeBinding> securitySchemes)
{
    public string RootNamespace { get; } = rootNamespace;
    public string ClientName { get; } = clientName;
    public string SerializerOptionsTypeName { get; } = serializerOptionsTypeName;
    public string Summary { get; } = summary;
    public string? Description { get; } = description;
    public string? ServerUrl { get; } = serverUrl;
    public IReadOnlyList<SchemaDefinition> Schemas { get; } = schemas;
    public IReadOnlyList<TagGroup> TagGroups { get; } = tagGroups;
    public IReadOnlyList<SecuritySchemeBinding> SecuritySchemes { get; } = securitySchemes;
    public bool HasDirectionalSchemaProperties { get; } = schemas.Any(static schema => schema.Properties.Any(static property => property.ReadOnly || property.WriteOnly));
}

internal sealed class TagGroup(string propertyName, string className, string? description, IReadOnlyList<OperationGroupItem> operations)
{
    public string PropertyName { get; } = propertyName;
    public string ClassName { get; } = className;
    public string? Description { get; } = description;
    public IReadOnlyList<OperationGroupItem> Operations { get; } = operations;
}

internal sealed class OperationGroupItem(
    string route,
    string operationType,
    string methodName,
    string summary,
    string? remarks,
    IReadOnlyList<ParameterInfo> parameters,
    RequestBodyInfo? requestBody,
    ResponseInfo response,
    IReadOnlyList<ErrorResponseInfo> errorResponses,
    IReadOnlyList<SecurityRequirementInfo>? securityRequirements)
{
    public string Route { get; } = route;
    public string OperationType { get; } = operationType;
    public string MethodName { get; } = methodName;
    public string Summary { get; } = summary;
    public string? Remarks { get; } = remarks;
    public IReadOnlyList<ParameterInfo> Parameters { get; } = parameters;
    public RequestBodyInfo? RequestBody { get; } = requestBody;
    public ResponseInfo Response { get; } = response;
    public IReadOnlyList<ErrorResponseInfo> ErrorResponses { get; } = errorResponses;
    public IReadOnlyList<SecurityRequirementInfo>? SecurityRequirements { get; } = securityRequirements;
}

internal sealed class ErrorResponseInfo(string statusCodePattern, ResponseInfo response)
{
    public string StatusCodePattern { get; } = statusCodePattern;
    public ResponseInfo Response { get; } = response;
}

internal sealed class SecurityRequirementInfo(IReadOnlyList<SecuritySchemeBinding> schemes)
{
    public IReadOnlyList<SecuritySchemeBinding> Schemes { get; } = schemes;
}

internal enum TypeShape
{
    Primitive,
    String,
    Enum,
    Object,
    Array,
    Dictionary,
    Binary,
    JsonElement
}

internal sealed class TypeUsage
{
    private TypeUsage(CSharpTypeReference csharpType, bool schemaAllowsNull)
    {
        CSharpType = csharpType;
        SchemaAllowsNull = schemaAllowsNull;
    }

    public CSharpTypeReference CSharpType { get; }
    public string NonNullableCSharpTypeName => CSharpType.NonNullableName;
    public TypeShape Shape => CSharpType.Shape;
    public bool SchemaAllowsNull { get; }
    public bool CanBeNullInCSharp => CSharpType.CanBeNull;
    public string CSharpTypeName => CSharpType.Name;
    public bool RequiresNonNullJsonResponse => CSharpType.RequiresNonNullJsonResponse;

    public static TypeUsage Create(string nonNullableCSharpTypeName, TypeShape shape, bool schemaAllowsNull, bool isOptional)
        => new(
            new CSharpTypeReference(nonNullableCSharpTypeName, shape, schemaAllowsNull || isOptional),
            schemaAllowsNull);
}

internal sealed class ParameterInfo(string wireName, string parameterName, TypeUsage type, bool required, ParameterLocation location, string? description)
{
    public string WireName { get; } = wireName;
    public string ParameterName { get; } = parameterName;
    public string ParameterTypeName { get; } = type.CSharpTypeName;
    public bool IsArray { get; } = type.Shape == TypeShape.Array;
    public bool Required { get; } = required;
    public ParameterLocation Location { get; } = location;
    public string? Description { get; } = description;
}

internal sealed class SchemaDefinition(
    string typeName,
    string declaredTypeName,
    string? parentTypeName,
    string? baseTypeName,
    string summary,
    string? description,
    string? dictionaryValueType,
    IReadOnlyList<SchemaPropertyDefinition> properties,
    string? discriminatorPropertyName,
    IReadOnlyList<SchemaDerivedTypeDefinition> derivedTypes,
    SchemaEnumKind enumKind,
    string? enumUnderlyingType,
    IReadOnlyList<SchemaEnumMemberDefinition> enumMembers)
{
    public string QualifiedTypeName { get; } = typeName;
    public string DeclaredTypeName { get; } = declaredTypeName;
    public string? ParentTypeName { get; } = parentTypeName;
    public string? BaseTypeName { get; } = baseTypeName;
    public string Summary { get; } = summary;
    public string? Description { get; } = description;
    public string? DictionaryValueType { get; } = dictionaryValueType;
    public IReadOnlyList<SchemaPropertyDefinition> Properties { get; } = properties;
    public string? DiscriminatorPropertyName { get; } = discriminatorPropertyName;
    public IReadOnlyList<SchemaDerivedTypeDefinition> DerivedTypes { get; } = derivedTypes;
    public SchemaEnumKind EnumKind { get; } = enumKind;
    public string? EnumUnderlyingType { get; } = enumUnderlyingType;
    public IReadOnlyList<SchemaEnumMemberDefinition> EnumMembers { get; } = enumMembers;
    public bool IsEnum { get; } = enumKind != SchemaEnumKind.None;
    public bool IsPolymorphicBase { get; } = !string.IsNullOrWhiteSpace(discriminatorPropertyName) && derivedTypes.Count > 0;
}

internal sealed class SchemaDerivedTypeDefinition(string typeName, string discriminatorValue)
{
    public string TypeName { get; } = typeName;
    public string DiscriminatorValue { get; } = discriminatorValue;
}

internal enum SchemaEnumKind
{
    None,
    String,
    Integer,
    Number
}

internal sealed class SchemaEnumMemberDefinition(string memberName, string value)
{
    public string MemberName { get; } = memberName;
    public string Value { get; } = value;
}

internal sealed class SchemaPropertyDefinition(
    string jsonPropertyName,
    string propertyName,
    TypeUsage type,
    bool required,
    bool readOnly,
    bool writeOnly,
    string summary,
    string? description)
{
    public string JsonPropertyName { get; } = jsonPropertyName;
    public string PropertyName { get; } = propertyName;
    public TypeUsage Type { get; } = type;
    public string PropertyTypeName { get; } = type.CSharpTypeName;
    public bool Required { get; } = required;
    public bool ReadOnly { get; } = readOnly;
    public bool WriteOnly { get; } = writeOnly;
    public string Summary { get; } = summary;
    public string? Description { get; } = description;
}

internal sealed class SecuritySchemeBinding(string schemeKey, string parameterName, string parameterDeclaration, string headerOrParameterName, SecuritySchemeLocation location, bool isBearerToken)
{
    public string SchemeKey { get; } = schemeKey;
    public string ParameterName { get; } = parameterName;
    public string ParameterDeclaration { get; } = parameterDeclaration;
    public string HeaderOrParameterName { get; } = headerOrParameterName;
    public SecuritySchemeLocation Location { get; } = location;
    public bool IsBearerToken { get; } = isBearerToken;
    public string FieldName { get; } = $"_{parameterName}";
}

internal enum SecuritySchemeLocation
{
    Header,
    Query,
    Cookie
}

internal sealed class RequestBodyInfo(
    RequestBodyKind kind,
    string contentType,
    TypeUsage type,
    string parameterName,
    bool isRequired,
    string? description,
    IReadOnlyList<RequestBodyPropertyInfo> properties)
{
    public RequestBodyKind Kind { get; } = kind;
    public string ContentType { get; } = contentType;
    public string BodyTypeName { get; } = type.CSharpTypeName;
    public string ParameterName { get; } = parameterName;
    public bool IsRequired { get; } = isRequired;
    public string? Description { get; } = description;
    public IReadOnlyList<RequestBodyPropertyInfo> Properties { get; } = properties;
}

internal sealed class ResponseInfo(ResponseKind kind, TypeUsage? type, string? documentation)
{
    public ResponseKind Kind { get; } = kind;
    public TypeUsage? Type { get; } = type;
    public string ResponseTypeName { get; } = type?.CSharpTypeName ?? string.Empty;
    public string? Documentation { get; } = documentation;
}

internal enum RequestBodyKind
{
    Json,
    FormUrlEncoded,
    MultipartFormData
}

internal enum ResponseKind
{
    None,
    Json,
    String,
    Binary
}

internal enum ParameterLocation
{
    Path,
    Query,
    Header,
    Cookie
}

internal enum RequestBodyValueKind
{
    Scalar,
    Binary,
    Collection
}

internal sealed class RequestBodyPropertyInfo(string wireName, string propertyName, RequestBodyValueKind kind, bool isNullable, RequestBodyValueKind? elementKind = null, bool isElementNullable = false)
{
    public string WireName { get; } = wireName;
    public string PropertyName { get; } = propertyName;
    public RequestBodyValueKind Kind { get; } = kind;
    public bool IsNullable { get; } = isNullable;
    public RequestBodyValueKind? ElementKind { get; } = elementKind;
    public bool IsElementNullable { get; } = isElementNullable;
}
