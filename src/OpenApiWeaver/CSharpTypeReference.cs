namespace OpenApiWeaver;

internal sealed class CSharpTypeReference(string nonNullableName, TypeShape shape, bool canBeNull)
{
    public string NonNullableName { get; } = nonNullableName;
    public TypeShape Shape { get; } = shape;
    public bool CanBeNull { get; } = canBeNull;
    public string Name { get; } = canBeNull
        ? CSharpUtilities.MakeNullableTypeName(nonNullableName)
        : nonNullableName;
    public bool RequiresNonNullJsonResponse { get; } = !canBeNull
        && shape is TypeShape.String or TypeShape.Object or TypeShape.Array or TypeShape.Dictionary;
}
