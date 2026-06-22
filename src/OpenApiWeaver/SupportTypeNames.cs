namespace OpenApiWeaver;

internal static class SupportTypeNames
{
    public const string ClientHelpers = "OpenApiClientHelpers";
    public const string Exception = "OpenApiException";
    public const string SerializerOptions = "SerializerOptions";
    public const string DefaultSerializerOptionsExpression = ClientHelpers + "." + SerializerOptions;

    public static IReadOnlyList<string> ReservedTypeNames { get; } =
    [
        ClientHelpers,
        Exception
    ];

    public static string CreateSerializerOptionsTypeName(string clientName)
        => clientName + "JsonSerializerOptions";

    public static void ReserveTypeNames(SchemaCatalog catalog)
    {
        foreach (var typeName in ReservedTypeNames)
        {
            catalog.ReserveTypeName(typeName);
        }
    }
}
