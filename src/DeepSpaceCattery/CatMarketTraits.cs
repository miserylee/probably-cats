using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const string NativeLuxuryItemType = "LUXURY_ITEM";
    private const int FullMarketValuePercent = 100;
    private const int CatBuyerBonusPerMatchPercent = 50;
    private const int OtherCatBuyerValuePercent = 70;
    private const string CatBuyerBuffId = "deep_space_cattery.cat_buyer_adjustment";

    private void SetCatMarketTraits(GameItem item, Cat cat, AgeStage stage)
    {
        if (item.IsGameItemType(NativeLuxuryItemType) || item.IsTag(NativeLuxuryItemType))
        {
            item.RemoveGameItemType(NativeLuxuryItemType);
        }
        bool live = cat.Alive && cat.IsInCare && stage != AgeStage.Dead;
        CatBuyerPreference? preference = live ? GetActivePreferenceForCat(item, cat, stage) : null;
        bool offered = preference != null;
        item.SetEnableDisableTag(CatSaleTag, offered);
        item.SetEnableDisableTag(ProtectedFromSale, !offered);
        item.SetValue(live ? GetCatMarketValue(cat, stage) : 0);
        int adjustment = preference is { PricePreferenceCount: > 0 } ?
            CatBuyerValuePercent(preference.CountPriceMatches(cat, stage)) - FullMarketValuePercent : 0;
        UpdateCatBuyerBuff(item, adjustment);
    }

    private static void UpdateCatBuyerBuff(GameItem item, int adjustment)
    {
        ItemFeature? existing = item.FindItemFeatureByID(CatBuyerBuffId);
        if (existing != null)
        {
            if (adjustment != 0 && !existing.isDisabled && existing.GetCurrentValueMod() == adjustment)
            {
                return;
            }
            item.RemoveItemFeature(existing);
        }
        if (adjustment == 0)
        {
            return;
        }

        string label = adjustment > 0 ? CatText.T("符合买家偏好") : CatText.T("未符合买家偏好");
        var buff = new ItemFeature(ItemFeature.FeatureType.ClientTemporary)
        {
            identifier = CatBuyerBuffId,
            category = CatBuyerBuffId,
            valueStage = ItemFeature.ValueStage.Final,
            isFeatureMatch = true,
            isExposable = false
        };
        buff.SetPublicDisplay(label).SetActualDisplay(label).SetValueModifier(adjustment)
            .SetPreExposedValueMod(adjustment, true);
        item.AddItemFeature(buff);
    }

    private static void ClearCatBuyerBuff(GameItem item) => UpdateCatBuyerBuff(item, 0);

    [HarmonyPatch(typeof(SoldItem), nameof(SoldItem.TrackItem))]
    private static class ClearSoldCatBuyerBuff
    {
        // The native tracker records the negotiated price before clearing the buyer's adjustment.
        private static void Postfix(GameItem? __0)
        {
            if (__0 != null)
            {
                ClearCatBuyerBuff(__0);
            }
        }
    }

    private CatBuyerPreference? GetActivePreferenceForCat(GameItem item, Cat cat, AgeStage stage) =>
        activeCatBuyerPreference is CatBuyerPreference preference && catsForCurrentBuyer.ContainsKey(item) &&
        preference.Accepts(cat, stage) && !item.IsTag(GeneralHelper.JUST_SOLD_TAG) &&
        GeneralHelper.IsItemEffectivelyOwned(item) ? preference : null;

    private static int CatBuyerValuePercent(int matches) =>
        matches > 0 ? FullMarketValuePercent + matches * CatBuyerBonusPerMatchPercent : OtherCatBuyerValuePercent;

    private static int GetCatMarketValue(Cat cat, AgeStage stage)
    {
        if (stage == AgeStage.Dead)
            return 0;
        return AppraiseCat(cat, stage).Total;
    }

    private static (int Total, int Age) AppraiseCat(Cat cat, AgeStage stage,
        List<(string Name, int Value)>? breakdown = null)
    {
        int adultValue = CatteryPrices.AdultBase + ValueAppearance(cat.GetPhenotype(), breakdown);
        int age = stage switch
        {
            AgeStage.Kitten => adultValue * 40 / 100,
            AgeStage.Senior => -(adultValue * 35 / 100),
            _ => 0
        };
        return (adultValue + age, age);
    }

    private static int ValueAppearance(Phenotype traits, List<(string Name, int Value)>? breakdown)
    {
        int total = 0;
        void Add(string name, int price)
        {
            if (price == 0)
                return;
            total += price;
            breakdown?.Add((name, price));
        }

        if (traits.FurLength == "hairless")
            Add(CatText.T("无毛"), 18);
        else
        {
            if (traits.FurLength == "long")
                Add(CatText.T("长毛"), 8);
            int color = traits.BaseColor switch
            {
                "silver" or "fawn" => 22,
                "lilac" or "cinnamon" or "charcoal" => 8,
                _ => 0
            };
            Add(ColorName(traits.BaseColor), color);
            if (traits.CoatPattern == 1 && traits.MarkingIndex == 0)
            {
                if (traits.BaseColor is "black" or "chalk")
                    Add(traits.BaseColor == "black" ? CatText.T("纯黑") : CatText.T("纯白"), 24);
                else
                    Add(CatText.F("纯色（{0}）", ColorName(traits.BaseColor)), 18);
            }
            else
            {
                string pattern = CoatPatternName(traits.CoatPattern);
                if (pattern.Length > 0)
                    Add(pattern, traits.CoatPattern switch { 2 => 5, 6 => 24, 7 => 20, 8 => 30, _ => 2 });
                var marking = CoatMarking(traits.Marking);
                Add(CatText.T(marking.Name), marking.Price);
            }
        }
        if (traits.EarShape == "folded")
            Add(CatText.T("折耳"), 8);
        else if (traits.EarShape == "slight")
            Add(CatText.T("微折耳"), 5);
        if (traits.EyeMode == "odd")
            Add(CatText.T("异瞳"), 10);
        if (traits.NosePigment == "spotted")
            Add(CatText.T("鼻斑"), -6);
        return total;
    }

    private void AddCatMarketTooltip(RichTextBuilder builder, GameItem item)
    {
        if (!IsCatItem(item))
        {
            return;
        }
        string id = ReadCatId(item);
        Cat? cat = cattery?.Cats.Find(candidate => candidate.Id == id);
        if (cat == null || !cat.Alive || !cat.IsInCare)
        {
            return;
        }
        int day = CurrentDay();
        AgeStage stage = cat.GetStage(day);
        if (stage == AgeStage.Dead)
        {
            return;
        }
        var breakdown = new List<(string Name, int Value)>();
        var price = AppraiseCat(cat, stage, breakdown);
        builder.AddLine(CatText.F("估价 {0} 信用点 · 基础 {1}", price.Total, CatteryPrices.AdultBase));
        if (breakdown.Count > 0)
            builder.AddLine(string.Join("，", breakdown.Select(part => $"{part.Name} {part.Value:+0;-0;0}")));
        if (price.Age != 0)
            builder.AddLine($"{(stage == AgeStage.Kitten ? CatText.T("幼年") : CatText.T("老年"))} {1:+0;-0;0}");
        if (GetActivePreferenceForCat(item, cat, stage) is CatBuyerPreference preference)
        {
            builder.AddLine(CatText.F("当前买家想找{0}。", preference.Description));
            if (preference.PricePreferenceCount > 0)
            {
                string matches = CatText.F("偏好满足 {0}/{1} 项", preference.CountPriceMatches(cat, stage), preference.PricePreferenceCount);
                builder.AddLine(CatText.F("{0} · 当前出价 {1} 信用点", matches, item.GetNegociatedValue()));
            }
            else
            {
                builder.AddLine(CatText.F("当前出价 {0} 信用点", item.GetNegociatedValue()));
            }
        }
        else if (activeCatBuyerPreference is { PurchaseRestriction: string restriction } buyer &&
            GeneralHelper.IsItemEffectivelyOwned(item) && !buyer.Accepts(cat, stage))
        {
            builder.AddLine(CatText.F("当前买家{0}。", restriction));
        }
        if (IsShowcaseCat(cat, item, day))
        {
            builder.AddLine(CatText.T("开门时会吸引一名爱猫人士，也可能吸引一名普通买家或卖家。"));
        }
    }
}
