namespace VsRecipeBrowser;
internal static class XrbProtocol
{
    // Wire compatibility is independent of the public mod name and release number.
    internal const int Version = 1;
    internal const string NearbyChannel = "xrb-nearby-items-v1";
    internal const string ChoiceChannel = "xrb-recipe-choice-v1";
    internal static bool Compatible(int peer) => peer == Version;
}
