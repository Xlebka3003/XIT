using System.Text.RegularExpressions;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VsRecipeBrowser;

internal sealed partial class RecipeOverlayDialog
{
    // Alternatives must remain valid for a complete source recipe.
    private List<ItemStack> CurrentIngredientOptions(int slot)
    {
        if (recipes.Count == 0) return new();
        int frameIndex = recipes[page].Frames is { Count: > 0 } frames ? ingredientVariantIndex % frames.Count : 0;
        if (ingredientOptionCache.TryGetValue((frameIndex, slot), out var cached)) return cached;
        var current = CurrentDisplayFrame();
        string role = IngredientRole(current, slot);
        var options = new List<ItemStack>();
        foreach (var frame in DisplayFrames())
        {
            if (IngredientRole(frame, slot) != role) continue;
            var candidates = frame.InputAlternatives?.ElementAtOrDefault(slot) ?? (frame.Inputs.ElementAtOrDefault(slot) is { } stack
                ? new List<ItemStack> { stack } : new List<ItemStack>());
            options.AddRange(candidates.Where(candidate => FrameAcceptsChoices(frame, new() { [slot] = candidate })));
        }
        cached = options.GroupBy(DisplayStackIdentity, StringComparer.Ordinal).Select(group => group.First().Clone()).ToList();
        cached = cached.OrderBy(DisplayStackIdentity, StringComparer.Ordinal).ToList();
        ingredientOptionCache[(frameIndex, slot)] = cached;
        if (current.Inputs.ElementAtOrDefault(slot)?.Collectible is { } tool
            && (tool.Tool == EnumTool.Saw || tool.Code.Path.StartsWith("saw-")))
        {
            foreach (var source in (current.SourceGridRecipes ?? new()).Where(source => GridIngredientAt(source, slot) is not null)
                .GroupBy(source => IngredientGroupKey(GridIngredientAt(source, slot)!)).Select(group => group.First()).Take(8))
            {
                var rule = GridIngredientAt(source, slot);
                if (rule is null) continue;
                var saws = capi.World.Collectibles.Where(item => item?.Code is not null
                    && (item.Tool == EnumTool.Saw || item.Code.Path.StartsWith("saw-"))).ToList();
                string Describe(CollectibleObject item)
                {
                    var stack = new ItemStack(item);
                    try { return $"{item.Code}:native={rule.SatisfiesAsIngredient(stack, false)},shared={IngredientRules.Accepts(rule, stack)}"; }
                    catch (Exception error) { return $"{item.Code}:error={error.GetType().Name}"; }
                }
                capi.Logger.Notification("[XRB Ingredient Audit] output={0}; slot={1}; size={2}x{3}; code={4}; match={5}; tags={6}; example={7}; consume={8}; durability={9}; alternatives={10}; candidates={11}",
                    current.Output.Collectible.Code, slot, source.Width, source.Height, rule.Code, rule.MatchingType,
                    rule.Tags, rule.ResolvedItemStack?.Collectible.Code, rule.Consume, rule.DurabilityChange,
                    string.Join(',', cached.Select(stack => stack.Collectible.Code)), string.Join(';', saws.Select(Describe)));
            }
        }
        return cached;
    }

    private readonly Dictionary<(int Frame, int Slot), List<ItemStack>> ingredientOptionCache = new();
    private RecipeDisplayEntry CurrentDisplayFrame() => recipes[page].Frames is { Count: > 0 } frames
        ? frames[ingredientVariantIndex % frames.Count] : recipes[page];
    private IEnumerable<RecipeDisplayEntry> DisplayFrames()
    {
        var recipe = recipes[page];
        if (recipe.Frames is not { Count: > 0 } frames) { yield return EnsureCurrentPageAlternatives(recipe); yield break; }
        for (int i = 0; i < frames.Count; i++) { frames[i] = EnsureCurrentPageAlternatives(frames[i]); yield return frames[i]; }
    }
    private static CraftingRecipeIngredient? GridIngredientAt(GridRecipe grid, int slot)
    {
        int width = Math.Clamp(grid.Width, 1, 3), height = Math.Clamp(grid.Height, 1, 3);
        int x = slot % 3 - (3 - width) / 2, y = slot / 3 - (3 - height) / 2;
        return x < 0 || y < 0 || x >= width || y >= height || grid.ResolvedIngredients is null ? null
            : grid.GetElementInGrid(y, x, grid.ResolvedIngredients, width);
    }
    private static string IngredientRole(RecipeDisplayEntry frame, int slot)
    {
        var stack = frame.Inputs.ElementAtOrDefault(slot);
        if (stack is null) return "empty";
        // A page can retain real routes using different classes/materials in
        // this cell (e.g. vine blocks vs reed items for rope). Source-recipe
        // validation, not their code prefix, decides which choice is legal.
        if (frame.Category == "Crafting" && !IsToolSlot(frame, slot)) return "craft-material-slot:" + slot;
        var ingredient = frame.SourceGridRecipes?.FirstOrDefault() is { } grid ? GridIngredientAt(grid, slot) : null;
        string family = stack.Collectible.Code.Path.Split('-')[0];
        if (ingredient is not null && IngredientRules.IsReusable(ingredient) && !ingredient.Tags.IsEmpty)
            family = "tool-tags:" + ingredient.Tags;
        if (family == "logsection") family = "log";
        return $"{stack.Class}:{IsToolSlot(frame, slot)}:{family}:{ingredient?.Name}";
    }
    private bool FrameAcceptsChoices(RecipeDisplayEntry frame, Dictionary<int, ItemStack> choices)
    {
        if (frame.SourceGridRecipes is { Count: > 0 } sources)
            return sources.Any(grid => choices.All(choice => GridIngredientAt(grid, choice.Key) is { } ingredient
                && IngredientAcceptsDisplay(ingredient, choice.Value)));
        return choices.All(choice => (frame.InputAlternatives?.ElementAtOrDefault(choice.Key)
            ?? (frame.Inputs.ElementAtOrDefault(choice.Key) is { } stack ? new List<ItemStack> { stack } : new()))
            .Any(stack => DisplayStackIdentity(stack) == DisplayStackIdentity(choice.Value)));
    }
    private void SelectIngredientOption(int slot, ItemStack selected)
    {
        string role = IngredientRole(CurrentDisplayFrame(), slot);
        var choices = new Dictionary<int, ItemStack>(selectedIngredients) { [slot] = selected.Clone() };
        var frames = DisplayFrames().ToList();
        int current = recipes[page].Frames is { Count: > 0 } ? ingredientVariantIndex % frames.Count : 0;
        var order = Enumerable.Range(0, frames.Count).OrderByDescending(index => index == current).ToList();
        int chosen = order.FirstOrDefault(index => IngredientRole(frames[index], slot) == role && FrameAcceptsChoices(frames[index], choices), -1);
        if (chosen < 0)
        {
            choices = new() { [slot] = selected.Clone() };
            chosen = order.FirstOrDefault(index => IngredientRole(frames[index], slot) == role && FrameAcceptsChoices(frames[index], choices), -1);
        }
        if (chosen < 0) return;
        selectedIngredients.Clear();
        foreach (var choice in choices) selectedIngredients[choice.Key] = choice.Value;
        if (recipes[page].Frames is { Count: > 0 }) ingredientVariantIndex = chosen;
        SetDisplayedIngredientVariants(recipes[page]);
        lastIngredientVariantChangeMs = capi.ElapsedMilliseconds;
    }

    private void OpenIngredientOptions(int slot)
    {
        var variants = CurrentIngredientOptions(slot);
        if (variants.Count <= 1) return;
        ingredientOptions?.TryClose(); ingredientOptions?.Dispose();
        ingredientOptions = new IngredientOptionsDialog(capi, variants, selected =>
        {
            SelectIngredientOption(slot, selected);
        });
        ingredientOptions.TryOpen();
    }

    private void ConfigureOptionsGrid(string key, System.Func<int, List<ItemStack>> options, Action<int, ItemStack> choose)
    {
        if (SingleComposer.GetElement(key) is not RecipeDisplayGridElement grid) return;
        grid.AlternativeCount = slot => options(slot).Count;
        grid.ShowAlternatives = slot =>
        {
            var variants = options(slot);
            if (variants.Count <= 1) return;
            ingredientOptions?.TryClose(); ingredientOptions?.Dispose();
            ingredientOptions = new IngredientOptionsDialog(capi, variants, selected => choose(slot, selected));
            ingredientOptions.TryOpen();
        };
    }

    private void ConfigureInputOptions(string key, System.Func<int, int> inputSlot)
    {
        if (SingleComposer.GetElement(key) is RecipeDisplayGridElement) ingredientGridSlots[key] = inputSlot;
        ConfigureOptionsGrid(key, slot => inputSlot(slot) is int index && index >= 0
            ? CurrentIngredientOptions(index) : new(), (slot, selected) => SelectIngredientOption(inputSlot(slot), selected));
    }
}
