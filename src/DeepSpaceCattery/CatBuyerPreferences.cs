using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const int BuyerPreferenceAvailableChance = 75;
    private const int ShowcaseAgeInfluenceChance = 50;
    private const int SingleBuyerRequirementChance = 65;
    private const int TripleBuyerRequirementChance = 5;
    [Flags]
    private enum BuyerRequirementGroup
    {
        Age = 1 << 0,
        Fur = 1 << 1,
        CoatPattern = 1 << 2,
        Ears = 1 << 3,
        Eyes = 1 << 4,
        Temperament = 1 << 5,
        Activity = 1 << 6
    }

    private static readonly CatBuyerRequirement KittenRequirement =
        new("幼年", BuyerRequirementGroup.Age, (stage, _) => stage == AgeStage.Kitten);
    private static readonly CatBuyerRequirement AdultRequirement =
        new("成年", BuyerRequirementGroup.Age, (stage, _) => stage == AgeStage.Adult);
    private static readonly CatBuyerRequirement SeniorRequirement =
        new("老年", BuyerRequirementGroup.Age, (stage, _) => stage == AgeStage.Senior);
    private static readonly CatBuyerRequirement SteadyRequirement =
        new("稳重", BuyerRequirementGroup.Temperament | BuyerRequirementGroup.Activity,
            (_, traits) => traits.Irritability <= 50 && traits.Activity <= 50);
    private static readonly CatBuyerRequirement PlayfulRequirement =
        new("活泼", BuyerRequirementGroup.Activity, (_, traits) => traits.Activity > 65);
    private static readonly CatBuyerRequirement WhiteCoatRequirement =
        new("纯白", BuyerRequirementGroup.CoatPattern, (_, traits) => traits.WhiteCoat);
    private static readonly CatBuyerRequirement TabbyCoatRequirement =
        new("虎斑", BuyerRequirementGroup.CoatPattern, (_, traits) => traits.FurLength != "hairless" && traits.CoatPattern == 3);
    private static readonly CatBuyerRequirement CalicoCoatRequirement =
        new("三花", BuyerRequirementGroup.CoatPattern, (_, traits) => traits.FurLength != "hairless" && traits.CoatPattern == 4);

    private static readonly CatBuyerRequirement SmokeRequirement =
        new("烟色", BuyerRequirementGroup.CoatPattern, (_, traits) => traits.CoatPattern == 6);
    private static readonly CatBuyerRequirement SilverShadedRequirement =
        new("银渐层", BuyerRequirementGroup.CoatPattern, (_, traits) => traits.CoatPattern == 7);
    private static readonly CatBuyerRequirement SilverShellRequirement =
        new("银壳色", BuyerRequirementGroup.CoatPattern, (_, traits) => traits.CoatPattern == 8);

    private static CatBuyerRequirement MarkingRequirement(string marking) =>
        new(CoatMarking(marking).Name, BuyerRequirementGroup.CoatPattern, (_, traits) => traits.Marking == marking);

    private static readonly CatBuyerRequirement[] CoatPreferences =
    {
        WhiteCoatRequirement, TabbyCoatRequirement, CalicoCoatRequirement, SmokeRequirement,
        SilverShadedRequirement, SilverShellRequirement, MarkingRequirement("tuxedo"), MarkingRequirement("point"),
        MarkingRequirement("snowpaws"), MarkingRequirement("inktail"), MarkingRequirement("inkseal")
    };

    private static readonly CatBuyerRequirement[] CatBuyerRequirements = new CatBuyerRequirement[]
    {
        KittenRequirement,
        AdultRequirement,
        SeniorRequirement,
        new("短毛", BuyerRequirementGroup.Fur, (_, traits) => traits.FurLength == "short"),
        new("长毛", BuyerRequirementGroup.Fur, (_, traits) => traits.FurLength == "long"),
        new("无毛", BuyerRequirementGroup.Fur, (_, traits) => traits.FurLength == "hairless", false),
        new("折耳", BuyerRequirementGroup.Ears, (_, traits) => traits.EarShape == "folded"),
        new("异瞳", BuyerRequirementGroup.Eyes, (_, traits) => traits.EyeMode == "odd"),
        new("亲人", BuyerRequirementGroup.Temperament, (_, traits) => traits.Affection > 65),
        new("性情温和", BuyerRequirementGroup.Temperament, (_, traits) => traits.Irritability <= 50),
        PlayfulRequirement,
        new("安静", BuyerRequirementGroup.Activity, (_, traits) => traits.Activity <= 50)
    }.Concat(CoatPreferences).ToArray();

    private static readonly CatBuyerPersona KittenOwner = new("想从小养猫的客人",
        "我想看着猫从小长大。", "只收幼猫", new[] { KittenRequirement });
    private static readonly CatBuyerPersona BusyOwner = new("忙碌的爱猫人士",
        "我平时忙，没多少时间照料小猫。", "只收成年猫", new[] { AdultRequirement });
    private static readonly CatBuyerPersona SeniorCompanion = new("来找陪伴的长辈",
        "退休后家里有点冷清，想养猫作伴。", "只收稳重的老年猫",
        new[] { SeniorRequirement, SteadyRequirement });
    private static readonly CatBuyerPersona PlaymateSeeker = new("爱逗猫的顾客",
        "下班回家，我就想和猫玩一会儿。", preferred: new[] { PlayfulRequirement });
    private static readonly CatBuyerPersona CoatFan = new("花色爱好者",
        "我看猫，头一眼就看它的花色。", preferred: CoatPreferences);

    private CatBuyerPreference? activeCatBuyerPreference;

    private sealed class CatBuyerPersona
    {
        private readonly string buyerNameKey;
        public string BuyerName => CatText.T(buyerNameKey);
        private readonly string introKey;
        public string Introduction => CatText.T(introKey);
        private readonly string? restrictionKey;
        public string? PurchaseRestriction => restrictionKey == null ? null : CatText.T(restrictionKey);
        public CatBuyerRequirement[] Required { get; }
        public CatBuyerRequirement[]? Preferred { get; }

        public CatBuyerPersona(string buyerName, string introduction, string? purchaseRestriction = null,
            CatBuyerRequirement[]? required = null, CatBuyerRequirement[]? preferred = null)
        {
            buyerNameKey = buyerName;
            introKey = introduction;
            restrictionKey = purchaseRestriction;
            Required = required ?? Array.Empty<CatBuyerRequirement>();
            Preferred = preferred;
        }
    }

    private sealed class CatBuyerRequirement
    {
        private readonly Func<AgeStage, Phenotype, bool> matches;
        private readonly bool canHaveCoatPattern;
        private readonly BuyerRequirementGroup group;

        private readonly string traitKey;
        public string Trait => CatText.T(traitKey);
        public bool IsAgeRestriction => group == BuyerRequirementGroup.Age;

        public CatBuyerRequirement(string trait, BuyerRequirementGroup group, Func<AgeStage, Phenotype, bool> matches,
            bool canHaveCoatPattern = true)
        {
            traitKey = trait;
            this.group = group;
            this.matches = matches;
            this.canHaveCoatPattern = canHaveCoatPattern;
        }

        public bool Matches(AgeStage stage, Phenotype traits) => stage != AgeStage.Dead && matches(stage, traits);

        public bool CanCombineWith(CatBuyerRequirement other) => (group & other.group) == 0 &&
            (canHaveCoatPattern || other.group != BuyerRequirementGroup.CoatPattern) &&
            (other.canHaveCoatPattern || group != BuyerRequirementGroup.CoatPattern);
    }

    private sealed class CatBuyerPreference
    {
        private readonly CatBuyerRequirement[] purchaseRestrictions;
        private readonly CatBuyerRequirement[] pricePreferences;
        private readonly CatBuyerPersona? persona;

        public string Description { get; }
        public string? ExtraPreferences { get; }
        public string? BuyerName => persona?.BuyerName;
        public string Introduction => persona?.Introduction ?? string.Empty;
        public string? PurchaseRestriction { get; }
        public int PricePreferenceCount => pricePreferences.Length;

        public CatBuyerPreference(CatBuyerRequirement[] requirements, CatBuyerPersona? persona)
        {
            this.persona = persona;
            var required = persona?.Required ?? Array.Empty<CatBuyerRequirement>();
            purchaseRestrictions = required.Union(requirements.Where(requirement => requirement.IsAgeRestriction)).ToArray();
            pricePreferences = requirements.Where(requirement => !requirement.IsAgeRestriction).ToArray();
            PurchaseRestriction = persona?.PurchaseRestriction;
            if (PurchaseRestriction == null && requirements.FirstOrDefault(requirement => requirement.IsAgeRestriction) is { } age)
            {
                PurchaseRestriction = age == KittenRequirement ? CatText.T("只收幼猫") : CatText.F("只收{0}猫", age.Trait);
            }
            Description = requirements.Length == 1 && requirements[0] == KittenRequirement
                ? CatText.T("幼猫")
                : CatText.F("{0}的猫", string.Join(CatText.T("、"), requirements.Select(requirement => requirement.Trait)));
            string extras = string.Join(CatText.T("、"), requirements.Skip(required.Length)
                .Where(requirement => !requirement.IsAgeRestriction).Select(requirement => requirement.Trait));
            ExtraPreferences = extras.Length == 0 ? null : extras;
        }

        public bool Accepts(Cat cat, AgeStage stage)
        {
            if (stage == AgeStage.Dead)
            {
                return false;
            }
            if (purchaseRestrictions.Length == 0)
            {
                return true;
            }
            Phenotype traits = cat.GetPhenotype();
            return purchaseRestrictions.All(requirement => requirement.Matches(stage, traits));
        }

        public int CountPriceMatches(Cat cat, AgeStage stage)
        {
            Phenotype traits = cat.GetPhenotype();
            return pricePreferences.Count(requirement => requirement.Matches(stage, traits));
        }
    }

    private CatBuyerPersona? ChooseCatBuyerPersona(AgeStage[] showcaseAges)
    {
        if (showcaseAges.Length > 0 && random.Next(100) < ShowcaseAgeInfluenceChance)
        {
            return showcaseAges[random.Next(showcaseAges.Length)] switch
            {
                AgeStage.Kitten => KittenOwner,
                AgeStage.Adult => BusyOwner,
                AgeStage.Senior => SeniorCompanion,
                _ => null
            };
        }
        return random.Next(100) switch
        {
            < 15 => KittenOwner,
            < 30 => BusyOwner,
            < 40 => SeniorCompanion,
            < 50 => PlaymateSeeker,
            < 60 => CoatFan,
            _ => null
        };
    }

    private CatBuyerPreference ChooseCatBuyerPreference(IEnumerable<(Cat Cat, GameItem Item)> offeredCats,
        int day, AgeStage[] showcaseAges)
    {
        CatBuyerPersona? persona = ChooseCatBuyerPersona(showcaseAges);
        int roll = random.Next(100);
        int count = roll < SingleBuyerRequirementChance ? 1 : roll >= 100 - TripleBuyerRequirementChance ? 3 : 2;
        CatBuyerRequirement[] required = persona?.Required ?? Array.Empty<CatBuyerRequirement>();
        var selected = new CatBuyerRequirement[Math.Max(count, required.Length)];
        required.CopyTo(selected, 0);
        for (int index = required.Length; index < selected.Length; index++)
        {
            var choices = index == 0 ? persona?.Preferred ?? CatBuyerRequirements :
                CatBuyerRequirements.Where(candidate => selected.Take(index).All(candidate.CanCombineWith)).ToArray();
            if (index == required.Length && choices.Length > 1 && random.Next(100) < BuyerPreferenceAvailableChance)
            {
                var available = offeredCats.Select(offer => (Stage: offer.Cat.GetStage(day), Traits: offer.Cat.GetPhenotype()))
                    .Where(cat => required.All(requirement => requirement.Matches(cat.Stage, cat.Traits))).ToArray();
                var matchingRequirements = choices.Where(requirement =>
                    available.Any(cat => requirement.Matches(cat.Stage, cat.Traits))).ToArray();
                if (matchingRequirements.Length > 0)
                {
                    choices = matchingRequirements;
                }
            }
            selected[index] = choices[random.Next(choices.Length)];
        }

        return new CatBuyerPreference(selected, persona);
    }
}
