using System.Text;

namespace OpenApiWeaver.CodeGeneration;

internal sealed class IndentedStringBuilder(StringBuilder builder, int indentLevel = 0)
{
    private int _indentLevel = indentLevel;
    private bool _isStartOfLine = true;

    public IndentedStringBuilder Append(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return this;
        }

        EnsureIndent();
        builder.Append(value);
        _isStartOfLine = value![value.Length - 1] is '\r' or '\n';
        return this;
    }

    public IndentedStringBuilder Append(char value)
    {
        EnsureIndent();
        builder.Append(value);
        _isStartOfLine = value is '\r' or '\n';
        return this;
    }

    public void AppendLine(string? value = null)
    {
        if (!string.IsNullOrEmpty(value))
        {
            Append(value);
        }

        builder.AppendLine();
        _isStartOfLine = true;
    }

    public IndentScope PushIndent()
    {
        _indentLevel++;
        return new IndentScope(this);
    }

    private void EnsureIndent()
    {
        if (!_isStartOfLine)
        {
            return;
        }

        builder.Append(' ', _indentLevel * 4);
        _isStartOfLine = false;
    }

    public readonly struct IndentScope(IndentedStringBuilder writer) : IDisposable
    {
        public void Dispose()
        {
            writer._indentLevel--;
        }
    }
}
