using System.Text;

namespace OpenApiWeaver;

public sealed partial class OpenApiWeaverSourceGenerator
{
    private sealed partial class ClientEmitter
    {
        private sealed class IndentedStringBuilder(StringBuilder builder, int indentLevel = 0)
        {
            private int _indentLevel = indentLevel;
            private bool _isStartOfLine = true;

            public StringBuilder Builder => builder;

            public string CurrentIndent => new(' ', _indentLevel * 4);

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

            public IDisposable PushIndent()
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

            private sealed class IndentScope(IndentedStringBuilder writer) : IDisposable
            {
                public void Dispose()
                {
                    writer._indentLevel--;
                }
            }
        }
    }
}
