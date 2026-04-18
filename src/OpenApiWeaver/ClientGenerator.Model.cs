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

    private sealed class ParameterInfo(string serializedName, string parameterName, string typeName, bool required, ParameterLocation location, string? description)
    {
        public string SerializedName { get; } = serializedName;
        public string ParameterName { get; } = parameterName;
        public string TypeName { get; } = typeName;
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
        public string TypeName { get; } = typeName;
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
        string jsonName,
        string propertyName,
        string typeName,
        bool required,
        string summary,
        string? description)
    {
        public string JsonName { get; } = jsonName;
        public string PropertyName { get; } = propertyName;
        public string TypeName { get; } = typeName;
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
        string typeName,
        bool isRequired,
        string? description,
        IReadOnlyList<RequestBodyPropertyInfo> properties)
    {
        public RequestBodyKind Kind { get; } = kind;
        public string TypeName { get; } = typeName;
        public bool IsRequired { get; } = isRequired;
        public string? Description { get; } = description;
        public IReadOnlyList<RequestBodyPropertyInfo> Properties { get; } = properties;
    }

    private sealed class ResponseInfo(ResponseKind kind, string typeName, string? documentation)
    {
        public ResponseKind Kind { get; } = kind;
        public string TypeName { get; } = typeName;
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

    private sealed class RequestBodyPropertyInfo(string serializedName, string propertyName, RequestBodyValueKind kind, bool nullable, RequestBodyValueKind? elementKind = null, bool elementNullable = false)
    {
        public string SerializedName { get; } = serializedName;
        public string PropertyName { get; } = propertyName;
        public RequestBodyValueKind Kind { get; } = kind;
        public bool Nullable { get; } = nullable;
        public RequestBodyValueKind? ElementKind { get; } = elementKind;
        public bool ElementNullable { get; } = elementNullable;
    }
}
