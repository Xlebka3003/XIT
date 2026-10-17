using System.Text;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VsRecipeBrowser;

[HarmonyPatch]
internal static class ModTooltipPatch
{
    private static Dictionary<string, string> modNames = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(ICoreClientAPI api)
    {
        modNames = api.ModLoader.Mods
            .GroupBy(mod => mod.Info.ModID, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Info.Name ?? group.Key, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        Type[] arguments = { typeof(ItemSlot), typeof(StringBuilder), typeof(IWorldAccessor), typeof(bool) };
        yield return AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemInfo), arguments);
        yield return AccessTools.Method(typeof(Item), nameof(Item.GetHeldItemInfo), arguments);
        yield return AccessTools.Method(typeof(Block), nameof(Block.GetHeldItemInfo), arguments);
    }

    [HarmonyPostfix]
    private static void AddModName(ItemSlot inSlot, StringBuilder dsc)
    {
        if (inSlot?.Inventory is not BrowserInventory || inSlot.Itemstack?.Collectible?.Code is null) return;
        string marker = BrowserText.Choose("\nМод: ", "\nMod: ");
        if (dsc.ToString().Contains(marker, StringComparison.Ordinal)) return;
        string modId = inSlot.Itemstack.Collectible.Code.Domain;
        string modName = modId == "game" ? "Vanilla" : modNames.TryGetValue(modId, out string? name) ? name : modId;
        dsc.Append(marker).Append("<font color=\"#A7A6E8\">").Append(System.Security.SecurityElement.Escape(modName)).Append("</font>");
    }
}
