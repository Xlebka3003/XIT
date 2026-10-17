using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace VsRecipeBrowser;

internal static class FavoriteIdentity
{
    internal static string Key(ItemStack stack)
    {
        string basic = $"{stack.Class}:{stack.Collectible.Code}";
        var attributes = stack.Attributes.Clone();
        foreach (string key in GlobalConstants.IgnoredStackAttributes) attributes.RemoveAttribute(key);
        attributes.RemoveAttribute("durability");
        attributes.RemoveAttribute("temperature");
        if (!attributes.Any()) return basic;
        string canonical = AttributesKey(attributes);
        return basic + "|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string AttributesKey(ITreeAttribute attributes)
    {
        try { return Sort(JObject.Parse(attributes.ToJsonToken())).ToString(Newtonsoft.Json.Formatting.None); }
        catch (Newtonsoft.Json.JsonException)
        {
            // Empty ItemstackAttribute values produce invalid JSON in the game.
            // Preserve their typed binary representation instead of losing data
            // or aborting inventory sorting/favorites/recipe grouping.
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
            WriteTree(attributes, writer);
            return "binary:" + Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        }
    }

    private static void WriteTree(ITreeAttribute attributes, BinaryWriter writer)
    {
        var entries = attributes.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        writer.Write(entries.Length);
        foreach (var entry in entries)
        {
            writer.Write(entry.Key);
            writer.Write(entry.Value?.GetAttributeId() ?? -1);
            if (entry.Value is ITreeAttribute tree) WriteTree(tree, writer);
            else entry.Value?.ToBytes(writer);
        }
    }

    private static JToken Sort(JToken token) => token switch
    {
        JObject obj => new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => new JProperty(p.Name, Sort(p.Value)))),
        JArray array => new JArray(array.Select(Sort)),
        _ => token.DeepClone()
    };
}
