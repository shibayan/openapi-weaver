namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed class ClientModel(
        string rootNamespace,
        string clientName,
        string summary,
        string? description,
        string? serverUrl,
        IReadOnlyList<SchemaDefinition> schemas,
        IReadOnlyList<TagGroup> tagGroups,
        IReadOnlyList<SecuritySchemeBinding> securitySchemes)
    {
        public string RootNamespace { get; } = rootNamespace;
        public string ClientName { get; } = clientName;
        public string Summary { get; } = summary;
        public string? Description { get; } = description;
        public string? ServerUrl { get; } = serverUrl;
        public IReadOnlyList<SchemaDefinition> Schemas { get; } = schemas;
        public IReadOnlyList<TagGroup> TagGroups { get; } = tagGroups;
        public IReadOnlyList<SecuritySchemeBinding> SecuritySchemes { get; } = securitySchemes;
    }

    private sealed class TagGroup(string propertyName, string className, string? description, IReadOnlyList<OperationGroupItem> operations)
    {
        public string PropertyName { get; } = propertyName;
        public string ClassName { get; } = className;
        public string? Description { get; } = description;
        public IReadOnlyList<OperationGroupItem> Operations { get; } = operations;
    }

    private sealed class OperationGroupItem(
        string route,
        string operationType,
        string methodName,
        string summary,
        string? remarks,
        IReadOnlyList<ParameterInfo> parameters,
        RequestBodyInfo? requestBody,
        ResponseInfo response,
        IReadOnlyList<ErrorResponseInfo> errorResponses)
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
        public bool HasParameters { get; } = parameters.Count > 0;
    }

    private sealed class ErrorResponseInfo(string statusCodePattern, ResponseInfo response)
    {
        public string StatusCodePattern { get; } = statusCodePattern;
        public ResponseInfo Response { get; } = response;
    }

    private enum TypeShape
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

    private sealed class TypeUsage(string nonNullableCSharpTypeName, TypeShape shape, bool schemaAllowsNull, bool isOptional)
    {
        public string NonNullableCSharpTypeName { get; } = nonNullableCSharpTypeName;
        public TypeShape Shape { get; } = shape;
        public bool SchemaAllowsNull { get; } = schemaAllowsNull;
        public bool IsOptional { get; } = isOptional;
        public bool CanBeNullInCSharp { get; } = schemaAllowsNull || isOptional;
        public string CSharpTypeName { get; } = schemaAllowsNull || isOptional
            ? MakeNullableTypeName(nonNullableCSharpTypeName)
            : nonNullableCSharpTypeName;
        public bool RequiresNonNullJsonResponse { get; } = !schemaAllowsNull
            && !isOptional
            && shape is TypeShape.String or TypeShape.Object or TypeShape.Array or TypeShape.Dictionary;
    }

    private sealed class ParameterInfo(string wireName, string parameterName, TypeUsage type, bool required, ParameterLocation location, string? description)
    {
        public string WireName { get; } = wireName;
        public string ParameterName { get; } = parameterName;
        public TypeUsage Type { get; } = type;
        public string ParameterTypeName { get; } = type.CSharpTypeName;
        public bool Required { get; } = required;
        public ParameterLocation Location { get; } = location;
        public string? Description { get; } = description;
    }

    private sealed class SchemaDefinition(
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

    private sealed class SchemaDerivedTypeDefinition(string typeName, string discriminatorValue)
    {
        public string TypeName { get; } = typeName;
        public string DiscriminatorValue { get; } = discriminatorValue;
    }

    private enum SchemaEnumKind
    {
        None,
        String,
        Integer
    }

    private sealed class SchemaEnumMemberDefinition(string memberName, string value)
    {
        public string MemberName { get; } = memberName;
        public string Value { get; } = value;
    }

    private sealed class SchemaPropertyDefinition(
        string jsonPropertyName,
        string propertyName,
        TypeUsage type,
        bool required,
        string summary,
        string? description)
    {
        public string JsonPropertyName { get; } = jsonPropertyName;
        public string PropertyName { get; } = propertyName;
        public TypeUsage Type { get; } = type;
        public string PropertyTypeName { get; } = type.CSharpTypeName;
        public bool Required { get; } = required;
        public string Summary { get; } = summary;
        public string? Description { get; } = description;
    }

    private sealed class SecuritySchemeBinding(string parameterName, string parameterDeclaration, string headerOrParameterName, SecuritySchemeLocation location, bool isBearerToken)
    {
        public string ParameterName { get; } = parameterName;
        public string ParameterDeclaration { get; } = parameterDeclaration;
        public string HeaderOrParameterName { get; } = headerOrParameterName;
        public SecuritySchemeLocation Location { get; } = location;
        public bool IsBearerToken { get; } = isBearerToken;
        public string FieldName { get; } = $"_{parameterName}";
    }

    private enum SecuritySchemeLocation
    {
        Header,
        Query,
        Cookie
    }

    private sealed class RequestBodyInfo(
        RequestBodyKind kind,
        TypeUsage type,
        string parameterName,
        bool isRequired,
        string? description,
        IReadOnlyList<RequestBodyPropertyInfo> properties)
    {
        public RequestBodyKind Kind { get; } = kind;
        public TypeUsage Type { get; } = type;
        public string BodyTypeName { get; } = type.CSharpTypeName;
        public string ParameterName { get; } = parameterName;
        public bool IsRequired { get; } = isRequired;
        public string? Description { get; } = description;
        public IReadOnlyList<RequestBodyPropertyInfo> Properties { get; } = properties;
    }

    private sealed class ResponseInfo(ResponseKind kind, TypeUsage? type, string? documentation)
    {
        public ResponseKind Kind { get; } = kind;
        public TypeUsage? Type { get; } = type;
        public string ResponseTypeName { get; } = type?.CSharpTypeName ?? string.Empty;
        public string? Documentation { get; } = documentation;
    }

    private enum RequestBodyKind
    {
        Json,
        FormUrlEncoded,
        MultipartFormData
    }

    private enum ResponseKind
    {
        None,
        Json,
        String,
        Binary
    }

    private enum ParameterLocation
    {
        Path,
        Query,
        Header,
        Cookie
    }

    private enum RequestBodyValueKind
    {
        Scalar,
        Binary,
        Collection
    }

    private sealed class RequestBodyPropertyInfo(string wireName, string propertyName, RequestBodyValueKind kind, bool isNullable, RequestBodyValueKind? elementKind = null, bool isElementNullable = false)
    {
        public string WireName { get; } = wireName;
        public string PropertyName { get; } = propertyName;
        public RequestBodyValueKind Kind { get; } = kind;
        public bool IsNullable { get; } = isNullable;
        public RequestBodyValueKind? ElementKind { get; } = elementKind;
        public bool IsElementNullable { get; } = isElementNullable;
    }
}
