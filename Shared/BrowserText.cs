using Vintagestory.API.Config;

namespace VsRecipeBrowser;

internal static class BrowserText
{
    public static string Choose(string russian, string english) =>
        ForLocale(Lang.CurrentLocale, russian, english);

    internal static string ForLocale(string? locale, string russian, string english) =>
        locale?.StartsWith("ru", StringComparison.OrdinalIgnoreCase) == true ? russian : english;
}
