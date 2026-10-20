using static OpenApiWeaver.CodeGeneration.CSharpCodeEmissionUtilities;

namespace OpenApiWeaver.CodeGeneration;

internal sealed class SchemaEmitter(ClientModel model)
{
    private readonly Dictionary<string, List<SchemaDefinition>> _nestedSchemasByParent = BuildNestedSchemaLookup(model.Schemas);

    public void Emit(IndentedStringBuilder writer)
    {
        foreach (var schema in model.Schemas)
        {
            if (schema.ParentTypeName is not null)
            {
                continue;
            }

            EmitSchemaDefinition(writer, schema);
            writer.AppendLine();
        }
    }

    private static Dictionary<string, List<SchemaDefinition>> BuildNestedSchemaLookup(IReadOnlyList<SchemaDefinition> schemas)
    {
        var lookup = new Dictionary<string, List<SchemaDefinition>>(StringComparer.Ordinal);
        foreach (var schema in schemas)
        {
            if (schema.ParentTypeName is null)
            {
                continue;
            }

            if (!lookup.TryGetValue(schema.ParentTypeName, out var children))
            {
                children = [];
                lookup[schema.ParentTypeName] = children;
            }

            children.Add(schema);
        }

        return lookup;
    }

    private static void EmitPolymorphicAttributes(IndentedStringBuilder writer, string discriminatorPropertyName, IReadOnlyList<SchemaDerivedTypeDefinition> derivedTypes)
    {
        // Unknown discriminator values fall back to the declared type so new server-side subtypes do not break clients.
        writer.Append("[JsonPolymorphic(TypeDiscriminatorPropertyName = \"").Append(EscapeStringLiteral(discriminatorPropertyName)).AppendLine("\", IgnoreUnrecognizedTypeDiscriminators = true)]");
        foreach (var derivedType in derivedTypes)
        {
            writer.Append("[JsonDerivedType(typeof(").Append(derivedType.TypeName).Append("), typeDiscriminator: \"").Append(EscapeStringLiteral(derivedType.DiscriminatorValue)).AppendLine("\")]");
        }
    }

    private void EmitSchemaDefinition(IndentedStringBuilder writer, SchemaDefinition schema)
    {
        if (schema.IsEnum)
        {
            EnumSchemaEmitter.Emit(writer, schema);
            return;
        }

        EmitSchema(writer, schema);
    }

    private void EmitSchema(IndentedStringBuilder writer, SchemaDefinition schema)
    {
        EmitDocComment(
            writer,
            summary: schema.Summary,
            remarks: schema.Description);

        if (schema.IsPolymorphicBase)
        {
            EmitPolymorphicAttributes(writer, schema.DiscriminatorPropertyName!, schema.DerivedTypes);
        }
        else if (schema.InheritedDiscriminatorPropertyName is not null && schema.DiscriminatorValue is not null)
        {
            // System.Text.Json only writes the discriminator when the declared type is polymorphic, so a derived
            // type registers itself to keep the discriminator when it is used directly (e.g. as a request body).
            EmitPolymorphicAttributes(
                writer,
                schema.InheritedDiscriminatorPropertyName,
                [new SchemaDerivedTypeDefinition(schema.DeclaredTypeName, schema.DiscriminatorValue)]);
        }

        if (DictionarySchemaConverterEmitter.RequiresConverter(schema))
        {
            writer.Append("[JsonConverter(typeof(").Append(schema.DeclaredTypeName).AppendLine("JsonConverter))]");
        }

        if (schema.DictionaryValueType is not null)
        {
            writer.Append("public sealed class ").Append(schema.DeclaredTypeName).Append(" : Dictionary<string, ").Append(schema.DictionaryValueType).AppendLine(">");
        }
        else
        {
            writer.Append(schema.IsPolymorphicBase || schema.DiscriminatorValue is not null ? "public class " : "public sealed class ").Append(schema.DeclaredTypeName);
            if (!string.IsNullOrWhiteSpace(schema.BaseTypeName))
            {
                writer.Append(" : ").Append(schema.BaseTypeName);
            }

            writer.AppendLine();
        }

        writer.AppendLine("{");
        using (writer.PushIndent())
        {
            foreach (var property in schema.Properties)
            {
                var usesPrivateInit = property.ReadOnly && !DictionarySchemaConverterEmitter.RequiresConverter(schema);
                var requiredModifier = property.Required && !property.ReadOnly ? "required " : string.Empty;
                EmitDocComment(
                    writer,
                    summary: property.Summary,
                    remarks: property.Description);
                if (property.ReadOnly)
                {
                    writer.AppendLine("[JsonInclude]");
                }

                if (!property.Required && property.Type.CanBeNullInCSharp)
                {
                    writer.AppendLine("[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]");
                }

                writer.Append("[JsonPropertyName(\"").Append(EscapeStringLiteral(property.JsonPropertyName)).AppendLine("\")]");
                var accessors = usesPrivateInit ? "{ get; private init; }" : "{ get; init; }";
                writer.Append("public ").Append(requiredModifier).Append(property.PropertyTypeName).Append(' ').Append(property.PropertyName).Append(' ').Append(accessors).AppendLine();
            }

            if (_nestedSchemasByParent.TryGetValue(schema.QualifiedTypeName, out var nestedSchemas))
            {
                foreach (var nestedSchema in nestedSchemas)
                {
                    writer.AppendLine();
                    EmitSchemaDefinition(writer, nestedSchema);
                }
            }
        }

        writer.AppendLine("}");

        if (DictionarySchemaConverterEmitter.RequiresConverter(schema))
        {
            writer.AppendLine();
            DictionarySchemaConverterEmitter.Emit(writer, schema, model.SerializerOptionsTypeName);
        }
    }
}
