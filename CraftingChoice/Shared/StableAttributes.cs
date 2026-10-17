using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using System.Security.Cryptography;
using System.Text;

namespace XrbRecipeChoice;

internal static class StableAttributes
{
    internal static string Key(ITreeAttribute attributes)
    {
        // The game's JSON for nested item stacks omits stack size even when
        // valid. Use typed data for containers to distinguish liquid amounts.
        if (ContainsItemStacks(attributes)) return BinaryKey(attributes);
        try { return Sort(JObject.Parse(attributes.ToJsonToken())).ToString(Formatting.None); }
        catch (JsonException) { return BinaryKey(attributes); }
    }

    private static bool ContainsItemStacks(ITreeAttribute tree) => tree.Any(entry => entry.Value is ItemstackAttribute
        || entry.Value is ITreeAttribute child && ContainsItemStacks(child));

    private static string BinaryKey(ITreeAttribute tree)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        WriteTree(tree, writer); writer.Flush();
        return "binary:" + Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteTree(ITreeAttribute tree, BinaryWriter writer)
    {
        var entries = tree.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        writer.Write(entries.Length);
        foreach (var entry in entries)
        {
            writer.Write(entry.Key); writer.Write(entry.Value?.GetAttributeId() ?? -1);
            if (entry.Value is ITreeAttribute child) WriteTree(child, writer);
            else entry.Value?.ToBytes(writer);
        }
    }

    private static JToken Sort(JToken token) => token switch
    {
        JObject obj => new JObject(obj.Properties().OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new JProperty(property.Name, Sort(property.Value)))),
        JArray array => new JArray(array.Select(Sort)),
        _ => token.DeepClone()
    };
}
