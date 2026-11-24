using HarmonyLib;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private static GameMod? catNamingOwner;

    [HarmonyPatch(typeof(LabelerBlueUIManager), nameof(LabelerBlueUIManager.OpenUI), typeof(GameItem))]
    private static class BlueLabelerCatName
    {
        private static bool Prefix(LabelerBlueUIManager __instance, GameItem __0)
        {
            GameMod? mod = catNamingOwner;
            if (mod?.enabled != true || !mod.IsCatItem(__0))
            {
                return true;
            }
            Cat? cat = mod.FindExaminedCat(__0);
            if (cat != null && GeneralHelper.IsItemEffectivelyOwned(__0))
            {
                __instance.OpenUI(cat.Name, name => mod.RenameCat(__0, cat, name),
                    () => mod.RenameCat(__0, cat, null));
            }
            return false;
        }
    }

    private void RenameCat(GameItem item, Cat cat, string? input)
    {
        if (!ReferenceEquals(FindExaminedCat(item), cat) || !GeneralHelper.IsItemEffectivelyOwned(item))
        {
            return;
        }

        if (SetCatName(item, cat, input))
        {
            SaveCattery();
        }
    }

    private static bool SetCatName(GameItem item, Cat cat, string? input)
    {
        string name = string.IsNullOrWhiteSpace(input) ? cat.OriginalName ?? cat.Name :
            input!.Trim().Replace('<', '‹').Replace('>', '›');
        if (name == cat.Name)
        {
            return false;
        }
        cat.OriginalName ??= cat.Name;
        cat.Name = name;
        item.name = name;
        return true;
    }

    private static bool ImportLegacyCatName(GameItem item, Cat cat)
    {
        if (!GeneralHelper.HasCustomName(item))
        {
            return false;
        }
        string label = GeneralHelper.GetCustomName(item);
        GeneralHelper.RemoveCustomName(item);
        SetCatName(item, cat, label);
        return true;
    }
}
