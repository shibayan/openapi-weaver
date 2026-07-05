using static OpenApiWeaver.CodeGeneration.CSharpCodeEmissionUtilities;

namespace OpenApiWeaver.CodeGeneration;

internal sealed partial class OperationEmitter
{
    private void EmitRouteTemplate(IndentedStringBuilder writer, string route, IReadOnlyList<ParameterInfo> pathParameters)
    {
        var parameterLookup = pathParameters
            .Where(static parameter => !string.IsNullOrEmpty(parameter.WireName))
            .ToDictionary(static parameter => parameter.WireName, StringComparer.Ordinal);

        var startIndex = 0;
        while (startIndex < route.Length)
        {
            var openBraceIndex = route.IndexOf('{', startIndex);
            if (openBraceIndex < 0)
            {
                EmitRouteLiteral(writer, route.Substring(startIndex));
                break;
            }

            var closeBraceIndex = route.IndexOf('}', openBraceIndex + 1);
            if (closeBraceIndex < 0)
            {
                EmitRouteLiteral(writer, route.Substring(startIndex));
                break;
            }

            EmitRouteLiteral(writer, route.Substring(startIndex, openBraceIndex - startIndex));

            var parameterName = route.Substring(openBraceIndex + 1, closeBraceIndex - openBraceIndex - 1);
            if (parameterLookup.TryGetValue(parameterName, out var parameter))
            {
                writer.Append("pathBuilder.Append(Uri.EscapeDataString(");
                EmitParameterValue(writer, parameter);
                writer.AppendLine("));");
            }
            else
            {
                EmitRouteLiteral(writer, route.Substring(openBraceIndex, closeBraceIndex - openBraceIndex + 1));
            }

            startIndex = closeBraceIndex + 1;
        }
    }

    private static void EmitRouteLiteral(IndentedStringBuilder writer, string segment)
    {
        if (segment.Length == 0)
        {
            return;
        }

        writer.Append("pathBuilder.Append(\"").Append(EscapeStringLiteral(segment)).AppendLine("\");");
    }

    private static string NormalizeRelativeRoute(string route)
    {
        return route.TrimStart('/');
    }

    private static void EmitQueryParameterAppend(IndentedStringBuilder writer, ParameterInfo parameter)
    {
        var encodedWireName = Uri.EscapeDataString(parameter.WireName);
        if (parameter.IsArray)
        {
            writer.Append("OpenApiClientHelpers.AppendQueryParameters(pathBuilder, ref hasQuery, \"").Append(EscapeStringLiteral(encodedWireName)).Append("\", ").Append(parameter.ParameterName).AppendLine(");");
            return;
        }

        writer.Append("OpenApiClientHelpers.AppendQueryParameter(pathBuilder, ref hasQuery, \"").Append(EscapeStringLiteral(encodedWireName)).Append("\", OpenApiClientHelpers.FormatParameter(").Append(parameter.ParameterName).AppendLine("));");
    }

    private static void EmitHeaderParameterAppend(IndentedStringBuilder writer, ParameterInfo parameter)
    {
        writer.Append("request.Headers.TryAddWithoutValidation(\"").Append(EscapeStringLiteral(parameter.WireName)).Append("\", ");
        EmitParameterValue(writer, parameter);
        writer.AppendLine(");");
    }

    private static void EmitCookieParameterAppend(IndentedStringBuilder writer, ParameterInfo parameter)
    {
        writer.Append("OpenApiClientHelpers.AppendCookieParameter(cookieBuilder, \"").Append(EscapeStringLiteral(Uri.EscapeDataString(parameter.WireName))).Append("\", ");
        EmitParameterValue(writer, parameter);
        writer.AppendLine(");");
    }

    private static void EmitParameterValue(IndentedStringBuilder writer, ParameterInfo parameter)
    {
        if (parameter.IsArray)
        {
            writer.Append("OpenApiClientHelpers.FormatCollectionParameter(").Append(parameter.ParameterName).Append(')');
            return;
        }

        writer.Append("OpenApiClientHelpers.FormatParameter(").Append(parameter.ParameterName).Append(')');
    }
}
