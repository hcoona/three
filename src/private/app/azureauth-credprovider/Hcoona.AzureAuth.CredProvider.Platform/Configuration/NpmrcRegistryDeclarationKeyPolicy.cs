namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal static class NpmrcRegistryDeclarationKeyPolicy
{
    private const string ScopedRegistrySuffix = ":registry";

    public static bool IsRegistryDeclarationKey(string key)
    {
        if (string.Equals(key, "registry", StringComparison.Ordinal))
        {
            return true;
        }

        if (
            !key.StartsWith('@')
            || !key.EndsWith(ScopedRegistrySuffix, StringComparison.Ordinal)
        )
        {
            return false;
        }

        int scopeEnd = key.Length - ScopedRegistrySuffix.Length;
        if (scopeEnd <= 1)
        {
            return false;
        }

        for (int index = 1; index < scopeEnd; index++)
        {
            if (!IsRouteableScopeCharacter(key[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRouteableScopeCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character)
        || character is '-' or '_' or '.' or '!' or '~' or '*' or '\'' or '(' or ')';
}
