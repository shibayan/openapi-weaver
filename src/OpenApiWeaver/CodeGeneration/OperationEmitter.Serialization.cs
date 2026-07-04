namespace OpenApiWeaver.CodeGeneration;

internal sealed partial class OperationEmitter
{
    private string GetSerializerOptionsExpression(JsonSerializerDirection direction)
        => JsonSerializerOptionsEmitter.GetOptionsExpression(_model, direction);
}
