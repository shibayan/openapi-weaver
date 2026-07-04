using Microsoft.OpenApi;

using OpenApiParameterLocation = Microsoft.OpenApi.ParameterLocation;

namespace OpenApiWeaver;

public sealed partial class ClientGenerator
{
    private sealed partial class Transformer
    {
        private List<SecuritySchemeBinding> BuildSecuritySchemes()
        {
            var bindings = new List<SecuritySchemeBinding>();
            if (_document.Components?.SecuritySchemes is null)
            {
                return bindings;
            }

            foreach (var scheme in _document.Components.SecuritySchemes)
            {
                var binding = CreateSecuritySchemeBinding(scheme.Key, scheme.Value);
                if (binding is not null)
                {
                    bindings.Add(binding);
                }
            }

            return bindings;
        }

        private IReadOnlyList<SecurityRequirementInfo>? ResolveOperationSecurityRequirements(
            OpenApiOperation operation,
            IReadOnlyDictionary<string, SecuritySchemeBinding> securitySchemesByKey)
        {
            var requirements = operation.Security ?? _document.Security;
            if (requirements is null)
            {
                return null;
            }

            if (requirements.Count == 0)
            {
                return [];
            }

            var result = new List<SecurityRequirementInfo>(requirements.Count);
            foreach (var requirement in requirements)
            {
                var schemes = new List<SecuritySchemeBinding>();
                foreach (var item in requirement)
                {
                    var schemeKey = item.Key.Reference?.Id ?? item.Key.Name;
                    if (schemeKey is not null && securitySchemesByKey.TryGetValue(schemeKey, out var binding))
                    {
                        schemes.Add(binding);
                    }
                }

                if (requirement.Count == 0 || schemes.Count == requirement.Count)
                {
                    result.Add(new SecurityRequirementInfo(schemes));
                }
            }

            return result;
        }

        private static SecuritySchemeBinding? CreateSecuritySchemeBinding(string schemeKey, IOpenApiSecurityScheme scheme)
        {
            if (scheme.Type == SecuritySchemeType.OAuth2
                || (scheme.Type == SecuritySchemeType.Http && string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase)))
            {
                var parameterName = CSharpUtilities.SafeIdentifier(CSharpUtilities.ToCamelCase(scheme.Type == SecuritySchemeType.OAuth2 ? "access_token" : $"{schemeKey}_token"));
                return new SecuritySchemeBinding(
                    schemeKey,
                    parameterName,
                    $"string? {parameterName} = default",
                    "Authorization",
                    SecuritySchemeLocation.Header,
                    isBearerToken: true);
            }

            if (scheme.Type == SecuritySchemeType.ApiKey)
            {
                var parameterName = CSharpUtilities.SafeIdentifier(CSharpUtilities.ToCamelCase($"{schemeKey}_api_key"));
                return new SecuritySchemeBinding(
                    schemeKey,
                    parameterName,
                    $"string? {parameterName} = default",
                    scheme.Name ?? parameterName,
                    MapLocation(scheme.In ?? OpenApiParameterLocation.Header),
                    isBearerToken: false);
            }

            return null;
        }

        private static SecuritySchemeLocation MapLocation(OpenApiParameterLocation location)
        {
            return location switch
            {
                OpenApiParameterLocation.Query => SecuritySchemeLocation.Query,
                OpenApiParameterLocation.Cookie => SecuritySchemeLocation.Cookie,
                _ => SecuritySchemeLocation.Header,
            };
        }
    }
}
