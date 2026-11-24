using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace DeepSpaceCattery;

public sealed class CatGeneScan
{
    public string[] Maternal { get; set; } = Array.Empty<string>();
    public string[] Paternal { get; set; } = Array.Empty<string>();
    public string MaternalX { get; set; } = string.Empty;
    public string PaternalSex { get; set; } = string.Empty;
    public int[] MaternalMutationMasks { get; set; } = Array.Empty<int>();
    public int[] PaternalMutationMasks { get; set; } = Array.Empty<int>();
}

public sealed partial class GameMod
{
    private const int PedigreeGenerations = 3;

    private static GameMod? pedigreeClickOwner;
    private string PedigreeWindowId => modId + ".cat_pedigree";
    private string ScanWindowId => modId + ".cat_gene_scan";

    private void RegisterCatPedigreeClick() => pedigreeClickOwner = this;

    private void UnregisterCatPedigreeClick()
    {
        if (ReferenceEquals(pedigreeClickOwner, this))
        {
            pedigreeClickOwner = null;
        }
    }

    // The default item activation path also handles configurable single-click actions.
    [HarmonyPatch(typeof(ItemMouseDoubleClickHandler), "DoubleClickAction")]
    private static class CatPedigreeDoubleClick
    {
        private static void Postfix(GameItem __0)
        {
            GameMod? mod = pedigreeClickOwner;
            if (__0 != null && mod?.enabled == true && mod.IsCatItem(__0))
            {
                mod.OpenCatPedigree(__0);
            }
        }
    }

    private void OpenCatPedigree(GameItem item)
    {
        Cat? cat = FindExaminedCat(item);
        var ui = CustomUIManager.Instance;
        if (cat == null || ui == null)
        {
            return;
        }

        int day = CurrentDay();
        CaptureCatGeneScan(item, cat);
        ShowPedigree(ui, cat, day, BuildAncestors(cat, cattery!.Cats));
    }

    private void ResetCatPedigree()
    {
        CustomUIManager.Instance?.CloseWindow(ScanWindowId);
        CustomUIManager.Instance?.CloseWindow(PedigreeWindowId);
    }

    private Cat? FindExaminedCat(GameItem item)
    {
        if (!enabled || (item.identifier != AdultCatId && item.identifier != KittenId) ||
            item.parentInventory == null || !HasCattery())
        {
            return null;
        }

        string id = ReadCatId(item);
        return string.IsNullOrEmpty(id) ? null :
            cattery!.Cats.Find(cat => cat.Id == id && cat.IsInCare && cat.GetStage(CurrentDay()) != AgeStage.Dead);
    }

    private void AddCatPedigreeTooltip(RichTextBuilder builder, GameItem item)
    {
        Cat? cat = FindExaminedCat(item);
        if (cat == null)
        {
            return;
        }

        int day = CurrentDay();
        CaptureCatGeneScan(item, cat);
        Phenotype traits = cat.GetPhenotype();
        builder.AddLine($"{(cat.IsMale ? CatText.T("公猫") : CatText.T("母猫"))} · {CatAgeDescription(cat, traits, day)}");
        builder.AddLine(CatText.F("饱腹度　{0}%", cat.Satiety));
        builder.AddLine(CatTemperament(traits));
        var statuses = new List<string>();
        if (cat.Satiety == 0) statuses.Add(CatText.T("[饥饿]"));
        if (cat.Neutered) statuses.Add(CatText.T("[已绝育]"));
        if (statuses.Count > 0) builder.AddLine(string.Join(" ", statuses));
        if (CatFoodBenefitLabel(cat, cat.GetStage(day), day) is string foodBenefit)
        {
            builder.AddLine(foodBenefit);
        }
        if (cattery!.GetPregnancy(cat.Id) is BreedingJob pregnancy)
        {
            var (elapsed, duration) = CatPregnancyProgress(pregnancy, day);
            builder.AddLine(CatText.F("怀孕进度　{0}/{1} 天", elapsed, duration));
        }
        if (!cat.IsMale && !cat.Neutered)
        {
            int remaining = cat.RecoveryDaysRemaining(day);
            builder.AddLine(remaining > 0 ? CatText.F("产后休整中　第 {0} 天可再配种", cat.NextBreedingDay) : CatText.F("产后配种间隔　{0} 天", traits.PostpartumRecoveryDays));
        }
        if (cat.GeneScan != null)
        {
            builder.AddLine(CatText.T("已测序 · 双击查看"));
        }
    }

    private static (int Elapsed, int Duration) CatPregnancyProgress(BreedingJob pregnancy, int day)
    {
        int duration = Math.Max(1, pregnancy.DueDay - pregnancy.StartedDay);
        return (Math.Clamp(day - pregnancy.StartedDay, 0, duration), duration);
    }

    private void CaptureCatGeneScan(GameItem item, Cat cat)
    {
        if (cat.GeneScan != null || activeStore?.isGeneScannerScanning != true || !GeneralHelper.IsItemOwned(item))
        {
            return;
        }

        Genome genome = cat.Genome;
        cat.GeneScan = new CatGeneScan
        {
            Maternal = (string[])genome.Maternal.Clone(),
            Paternal = (string[])genome.Paternal.Clone(),
            MaternalX = genome.MaternalX,
            PaternalSex = genome.PaternalSex,
            MaternalMutationMasks = (int[])genome.MaternalMutationMasks.Clone(),
            PaternalMutationMasks = (int[])genome.PaternalMutationMasks.Clone()
        };
        SaveCattery();
    }

    private static List<Cat?[]> BuildAncestors(Cat cat, IEnumerable<Cat> records)
    {
        var byId = records.ToLookup(record => record.Id, StringComparer.Ordinal);
        var generations = new List<Cat?[]> { new Cat?[] { cat } };
        for (int depth = 1; depth <= PedigreeGenerations; depth++)
        {
            Cat?[] children = generations[depth - 1];
            var parents = new Cat?[2 * children.Length];
            for (int index = 0; index < children.Length; index++)
            {
                Cat? child = children[index];
                if (child == null)
                {
                    continue;
                }

                parents[2 * index] = KnownParent(child.MotherId);
                parents[2 * index + 1] = KnownParent(child.FatherId);

                Cat? KnownParent(string? id)
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        return null;
                    }
                    for (int row = depth - 1, branch = index; row >= 0; row--, branch /= 2)
                    {
                        if (generations[row][branch]?.Id == id)
                        {
                            return null;
                        }
                    }
                    return byId[id].FirstOrDefault();
                }
            }

            if (parents.All(parent => parent == null))
            {
                break;
            }
            generations.Add(parents);
        }
        return generations;
    }

    private static string AncestorRole(int depth, int slot) => depth switch
    {
        0 => CatText.T("本猫"),
        1 => slot == 0 ? CatText.T("母亲") : CatText.T("父亲"),
        2 => slot < 2 ? (slot == 0 ? CatText.T("外祖母") : CatText.T("外祖父")) : (slot == 2 ? CatText.T("祖母") : CatText.T("祖父")),
        _ => slot % 2 == 0 ? CatText.F("{0}之母", AncestorRole(2, slot / 2)) : CatText.F("{0}之父", AncestorRole(2, slot / 2))
    };

    private static string PedigreeStageName(Cat cat, AgeStage stage) =>
        cat.WasAbandoned ? CatText.T("已遗弃") : cat.WasSold ? CatText.T("已离店") : StageName(stage);

    private static string CatAgeDescription(Cat cat, Phenotype traits, int day)
    {
        AgeStage stage = cat.GetStage(day);
        string? nextStage = stage switch
        {
            AgeStage.Kitten => CatText.T("距成年"),
            AgeStage.Adult => CatText.T("距老年"),
            AgeStage.Senior => CatText.T("预计剩余寿命"),
            _ => null
        };
        return nextStage == null ? PedigreeStageName(cat, stage) :
            CatText.F("{0} · {1} {2} 天", StageName(stage), nextStage, cat.DaysUntilNextStage(day, traits, stage));
    }

    private static string? CatFoodBenefitLabel(Cat cat, AgeStage stage, int day)
    {
        if (day == int.MaxValue || !cat.HasCatFoodBenefits(day + 1))
        {
            return null;
        }
        return stage switch
        {
            AgeStage.Kitten => CatText.T("猫粮 · 成长加快"),
            AgeStage.Adult => CatText.T("猫粮 · 延缓衰老"),
            AgeStage.Senior => CatText.T("猫粮 · 夜游好运"),
            _ => null
        };
    }

    private static string TraitGrade(int value, string low, string middle, string high) =>
        value <= 50 ? low : value <= 65 ? middle : high;

    private static string CatTemperament(Phenotype traits) => string.Join(" · ",
        TraitGrade(traits.Affection, CatText.T("较独立"), CatText.T("容易亲近"), CatText.T("亲人")),
        TraitGrade(traits.Irritability, CatText.T("性情温和"), CatText.T("有脾气"), CatText.T("容易烦躁")),
        TraitGrade(traits.Activity, CatText.T("安静"), CatText.T("爱走动"), CatText.T("活泼")));

}
