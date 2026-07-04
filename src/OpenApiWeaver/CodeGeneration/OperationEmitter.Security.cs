namespace OpenApiWeaver.CodeGeneration;

internal sealed partial class OperationEmitter
{
    private void EmitSecurityRequirementSelection(IndentedStringBuilder writer, OperationGroupItem operation)
    {
        if (operation.SecurityRequirements is not { Count: > 0 } securityRequirements
            || !securityRequirements.Any(static requirement => requirement.Schemes.Count > 0))
        {
            return;
        }

        writer.AppendLine("var securityRequirementIndex = 0;");
        for (var i = 0; i < securityRequirements.Count; i++)
        {
            writer.Append("if (securityRequirementIndex == 0");
            var requirement = securityRequirements[i];
            foreach (var scheme in requirement.Schemes)
            {
                writer.Append(" && ").Append(scheme.FieldName).Append(" is not null");
            }

            writer.AppendLine(")");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                writer.Append("securityRequirementIndex = ").Append((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(";");
            }

            writer.AppendLine("}");
        }
    }

    private void EmitSecuritySchemeBlock(IndentedStringBuilder writer, OperationGroupItem operation, SecuritySchemeBinding securityScheme, Action emitBody)
    {
        if (operation.SecurityRequirements is null)
        {
            writer.Append("if (").Append(securityScheme.FieldName).AppendLine(" is not null)");
            writer.AppendLine("{");
            using (writer.PushIndent())
            {
                emitBody();
            }

            writer.AppendLine("}");
            return;
        }

        var matchingRequirements = new List<int>();
        for (var i = 0; i < operation.SecurityRequirements.Count; i++)
        {
            if (operation.SecurityRequirements[i].Schemes.Contains(securityScheme))
            {
                matchingRequirements.Add(i + 1);
            }
        }

        if (matchingRequirements.Count == 0)
        {
            return;
        }

        writer.Append("if (");
        for (var i = 0; i < matchingRequirements.Count; i++)
        {
            if (i > 0)
            {
                writer.Append(" || ");
            }

            writer.Append("securityRequirementIndex == ").Append(matchingRequirements[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        writer.AppendLine(")");
        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            emitBody();
        }

        writer.AppendLine("}");
    }

    private List<SecuritySchemeBinding> GetOperationSecuritySchemes(OperationGroupItem operation, SecuritySchemeLocation location)
        => GetOperationSecuritySchemes(operation, static (scheme, state) => scheme.Location == state, location);

    private List<SecuritySchemeBinding> GetOperationSecuritySchemesExcept(OperationGroupItem operation, SecuritySchemeLocation location)
        => GetOperationSecuritySchemes(operation, static (scheme, state) => scheme.Location != state, location);

    private List<SecuritySchemeBinding> GetOperationSecuritySchemes<TState>(
        OperationGroupItem operation,
        Func<SecuritySchemeBinding, TState, bool> predicate,
        TState state)
    {
        var schemes = operation.SecurityRequirements is null
            ? _model.SecuritySchemes
            : operation.SecurityRequirements.SelectMany(static requirement => requirement.Schemes).ToList();

        var result = new List<SecuritySchemeBinding>();
        var usedSchemeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scheme in schemes)
        {
            if (predicate(scheme, state) && usedSchemeKeys.Add(scheme.SchemeKey))
            {
                result.Add(scheme);
            }
        }

        return result;
    }
}
