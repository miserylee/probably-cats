using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DeepSpaceCattery;

public sealed partial class GameMod
{
    private const int PedigreeCanvasWidth = 876;
    private const int PedigreeCanvasHeight = 422;
    private const int PedigreeTreeWidth = 548;
    private const int PedigreeDetailWidth = 316;
    private const int PedigreeNodeHeight = 70;

    private static readonly Color PedigreeBackground = new Color32(22, 33, 30, 255);
    private static readonly Color PedigreePanel = new Color32(31, 45, 39, 255);
    private static readonly Color PedigreeCard = new Color32(41, 59, 51, 255);
    private static readonly Color PedigreeCardLight = new Color32(52, 72, 60, 255);
    private static readonly Color PedigreeLine = new Color32(67, 88, 77, 255);
    private static readonly Color PedigreeInk = new Color32(237, 241, 227, 255);
    private static readonly Color PedigreeMuted = new Color32(174, 190, 176, 255);
    private static readonly Color PedigreeGold = new Color32(245, 203, 122, 255);
    private static readonly Color PedigreeMaternal = new Color32(234, 160, 145, 255);
    private static readonly Color PedigreePaternal = new Color32(133, 202, 209, 255);
    private static readonly Color MutationBorder = new Color32(209, 136, 250, 255);
    private static readonly Color PedigreeMint = new Color32(159, 212, 155, 255);

    private void ShowPedigree(CustomUIManager ui, Cat focus, int day, IReadOnlyList<Cat?[]> generations)
    {
        var builder = CustomUIManager.Window(PedigreeWindowId, CatText.T("遗传图"), CustomUIManager.LAYER_PAPER)
            .SetSize(920, 500)
            .SetDraggable(true)
            .Center()
            .OnClose(() => ui.CloseWindow(ScanWindowId));
        var surface = PedigreeCanvas.Create(builder, ui, PedigreeCanvasWidth, PedigreeCanvasHeight);
        var chart = surface.Section("亲缘图", 0, 0, PedigreeTreeWidth, PedigreeCanvasHeight, PedigreeBackground);
        var detail = surface.Section("个体详情", PedigreeTreeWidth + 12, 0, PedigreeDetailWidth, PedigreeCanvasHeight, PedigreePanel);
        chart.Text(CatText.T("血亲"), 16, 11, 170, 23, 20, PedigreeInk);
        chart.Text(generations.Count == 1 ? CatText.T("暂无已知祖先") : CatText.T("点击头像查看性状"), 16, 36, 180, 16, 12, PedigreeMuted);
        if (generations.Count > 1)
        {
            chart.Badge(CatText.T("母系"), 411, 12, 57, PedigreeMaternal);
            chart.Badge(CatText.T("父系"), 474, 12, 57, PedigreePaternal);
        }
        chart.Box("分隔线", 15, 55, PedigreeTreeWidth - 30, 1, PedigreeLine);

        var cards = new List<(Cat Cat, Image Outer, Image Fill, Color Branch)>();
        if (generations.Count > 1)
        {
            DrawPedigreeConnections(chart, generations);
        }
        for (int depth = generations.Count - 1; depth >= 0; depth--)
        {
            Cat?[] row = generations[depth];
            for (int slot = 0; slot < row.Length; slot++)
            {
                Cat? cat = row[slot];
                if (cat == null)
                {
                    continue;
                }
                string role = AncestorRole(depth, slot);
                float width = generations.Count == 1 ? 138 : depth == 0 ? 102 : 60;
                float height = generations.Count == 1 ? 152 : PedigreeNodeHeight;
                float x = PedigreeNodeCenter(slot, row.Length) - width / 2;
                float y = generations.Count == 1 ? 127 : PedigreeRowTop(generations.Count, depth);
                Color branch = depth == 0 ? PedigreeGold : PedigreeBranchColor(slot, row.Length);
                var outer = chart.Box("亲缘卡轮廓", x, y, width, height, branch);
                var card = chart.Section("亲缘卡", x + 1, y + 1, width - 2, height - 2, PedigreeCard);
                cards.Add((cat, outer, card.Background, branch));
                AgeStage stage = cat.GetStage(day);
                float imageHeight = height - 26;
                card.Portrait(PedigreePortrait(ui, cat, stage), 3, 1, width - 8, imageHeight);
                card.Text(ClipName(cat.Name, depth == 0 ? 15 : 8), 2, height - 28, width - 6, 14, 12, PedigreeInk, center: true);
                card.Text(depth == 3 ? CatText.T("曾祖辈") : role, 2, height - 14, width - 6, 11, 9, branch, center: true);
                if (cat.GeneScan != null)
                {
                    card.Box("已测序", width - 12, 6, 5, 5, PedigreeMint);
                }
                card.MakeInteractive(() => Pick(cat, role),
                    $"{cat.Name} · {role}\n{PedigreeStageName(cat, stage)} · {DescribeAppearance(cat.GetPhenotype())}" +
                    (cat.GeneScan != null ? CatText.T("\n已测序") : ""));
            }
        }

        Pick(focus, CatText.T("本猫"));
        builder.Show();

        void Pick(Cat selected, string relationship)
        {
            foreach (var entry in cards)
            {
                bool highlighted = ReferenceEquals(selected, entry.Cat);
                entry.Outer.color = highlighted ? PedigreeGold : entry.Branch * new Color(0.66f, 0.66f, 0.66f, 1);
                entry.Fill.color = highlighted ? PedigreeCardLight : PedigreeCard;
            }
            ui.CloseWindow(ScanWindowId);
            CustomUIBuilder.ClearContainer(detail.Root);
            DrawCatDetail(ui, detail, selected, relationship, day, ReferenceEquals(selected, focus));
        }
    }

    private static float PedigreeNodeCenter(int slot, int slots) => 13 + (slot + 0.5f) * (PedigreeTreeWidth - 26) / slots;

    private static float PedigreeRowTop(int count, int depth) => 65 + (4 - count) * 29 + (count - depth - 1) * 91;

    private static Color PedigreeBranchColor(int slot, int slots) => slot < slots / 2 ? PedigreeMaternal : PedigreePaternal;

    private static void DrawPedigreeConnections(PedigreeCanvas chart, IReadOnlyList<Cat?[]> generations)
    {
        for (int depth = 1; depth < generations.Count; depth++)
        {
            Cat?[] upper = generations[depth];
            float top = PedigreeRowTop(generations.Count, depth) + PedigreeNodeHeight;
            float bottom = PedigreeRowTop(generations.Count, depth - 1);
            float middle = (top + bottom) / 2;
            for (int slot = 0; slot < upper.Length; slot++)
            {
                if (upper[slot] == null)
                {
                    continue;
                }
                float from = PedigreeNodeCenter(slot, upper.Length);
                float to = PedigreeNodeCenter(slot / 2, upper.Length / 2);
                if (depth == 1)
                {
                    to += slot == 0 ? -8 : 8;
                }
                Color branch = PedigreeBranchColor(slot, upper.Length);
                chart.Box("亲缘连线", from - 1, top, 2, middle - top + 1, branch);
                chart.Box("亲缘连线", Math.Min(from, to) - 1, middle, Math.Abs(from - to) + 2, 2, branch);
                chart.Box("亲缘连线", to - 1, middle, 2, bottom - middle, branch);
            }
        }
    }

    private Sprite? PedigreePortrait(CustomUIManager ui, Cat cat, AgeStage stage)
    {
        if (stage == AgeStage.Dead)
        {
            double daysOld = cat.EffectiveAgeDays(cat.DeathDay ?? CurrentDay());
            stage = daysOld < cat.GetPhenotype().JuvenileDays ? AgeStage.Kitten : AgeStage.Adult;
        }
        return portraits?.Get(cat, stage) ??
            ui.GetSprite(SpriteKey(stage == AgeStage.Kitten ? "cattery_kitten" : "cattery_adult"), quiet: true);
    }

    private void DrawCatDetail(CustomUIManager ui, PedigreeCanvas detail, Cat cat, string relationship, int day, bool focus)
    {
        AgeStage stage = cat.GetStage(day);
        bool inStore = cat.IsInCare && stage != AgeStage.Dead;
        Phenotype traits = cat.GetPhenotype();
        var hero = detail.Section("个体卡", 10, 10, 296, 116, PedigreeCard);
        hero.Box("立绘底色", 6, 5, 85, 106, PedigreeBackground);
        hero.Portrait(PedigreePortrait(ui, cat, stage), 8, 7, 81, 102);
        hero.Text(ClipName(cat.Name, 16), 101, 7, 187, 26, 22, PedigreeInk);
        hero.Text($"{relationship} · {(cat.IsMale ? CatText.T("公猫") : CatText.T("母猫"))}", 101, 36, 187, 15, 12, PedigreeGold);
        hero.Text(inStore ? CatAgeDescription(cat, traits, day) : PedigreeStageName(cat, stage),
            101, 54, 187, 16, 13, PedigreeInk);
        if (inStore)
        {
            int satiety = Math.Clamp(cat.Satiety, 0, 100);
            AddCatStatusProgress(hero, CatText.F("饱腹 {0}%", satiety), 74, satiety / 100f,
                satiety <= 25 ? PedigreeMaternal : PedigreeMint);
            int remaining = cat.RecoveryDaysRemaining(day);
            if (cattery!.GetPregnancy(cat.Id) is BreedingJob pregnancy)
            {
                var (elapsed, duration) = CatPregnancyProgress(pregnancy, day);
                AddCatStatusProgress(hero, CatText.F("怀孕 {0}/{1} 天", elapsed, duration), 93, (float)elapsed / duration, PedigreeGold);
            }
            else if (remaining > 0)
            {
                AddCatStatusProgress(hero, CatText.F("距可配种 {0} 天", remaining), 93,
                    1f - (float)remaining / (traits.PostpartumRecoveryDays + 1), PedigreeMint);
            }
        }

        var look = detail.Section("外观", 10, 135, 296, 70, PedigreeCard);
        look.Text(CatText.T("外观"), 9, 5, 274, 16, 12, PedigreeGold);
        string[] appearance = DescribeAppearance(traits).Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries);
        look.Text(string.Join(" · ", appearance.Take(3)), 9, 25, 277, 16, 13, PedigreeInk);
        look.Text(string.Join(" · ", appearance.Skip(3)), 9, 44, 277, 15, 12, PedigreeMuted);

        var temperament = detail.Section("性格", 10, 214, 144, 111, PedigreeCard);
        AddTraitCards(temperament, CatText.T("性格"), PedigreeMaternal,
            TraitGrade(traits.Affection, CatText.T("较独立"), CatText.T("容易亲近"), CatText.T("亲人")),
            TraitGrade(traits.Irritability, CatText.T("性情温和"), CatText.T("有脾气"), CatText.T("容易烦躁")),
            TraitGrade(traits.Activity, CatText.T("安静"), CatText.T("爱走动"), CatText.T("活泼")),
            TraitGrade(traits.Curiosity, CatText.T("谨慎探索"), CatText.T("爱探索"), CatText.T("好奇心强")));
        var routines = detail.Section("习性", 162, 214, 144, 111, PedigreeCard);
        AddTraitCards(routines, CatText.T("习性"), PedigreePaternal,
            TraitGrade(traits.Nocturnality, CatText.T("少夜游"), CatText.T("偶尔夜游"), CatText.T("爱夜游")),
            TraitGrade(traits.HuntingDrive, CatText.T("少追猎"), CatText.T("会追猎"), CatText.T("爱追猎")),
            TraitGrade(traits.Reaction, CatText.T("反应平缓"), CatText.T("反应较快"), CatText.T("反应敏捷")),
            TraitGrade(traits.Vigilance, CatText.T("较放松"), CatText.T("警觉"), CatText.T("高度警觉")));

        var badges = new List<(string Text, float Width, Color Color)>();
        if (cat.Neutered) badges.Add((CatText.T("已绝育"), 62, PedigreeMaternal));
        if (traits.SlowRecovery) badges.Add((CatText.T("恢复较慢"), 70, PedigreePaternal));
        if (!cat.IsMale && !cat.Neutered) badges.Add((CatText.T("窝产潜力 ") + BirthPotential(traits.LitterPotential), 100, PedigreeMint));
        if (inStore && CatFoodBenefitLabel(cat, stage, day) is string foodBenefit)
        {
            badges.Add((foodBenefit, 113, PedigreeGold));
        }
        float badgeX = 10;
        foreach (var badge in badges)
        {
            detail.Badge(badge.Text, badgeX, 335, badge.Width, badge.Color);
            badgeX += badge.Width + 5;
        }

        CatGeneScan? scan = cat.GeneScan;
        var record = detail.Section("测序入口", 10, 367, 296, 43, scan != null ? PedigreeCardLight : PedigreeCard);
        record.Text(scan != null ? CatText.T("查看测序结果") : CatText.T("暂无测序记录"), 9, scan != null ? 13 : 5, 245, 16, 14,
            scan != null ? PedigreeMint : PedigreeInk);
        if (scan == null)
        {
            record.Text(focus ? CatText.T("开启扫描仪后查看这只猫") : CatText.T("这只血亲尚未测序"), 9, 24, 275, 14, 11, PedigreeMuted);
        }
        else
        {
            record.Text("›", 258, 7, 28, 27, 24, PedigreeMint, center: true);
            record.MakeInteractive(() => ShowCatScan(ui, cat, scan));
        }
    }

    private static void AddCatStatusProgress(PedigreeCanvas card, string label, int y, float progress, Color tint)
    {
        const float width = 72;
        card.Text(label, 101, y, 107, 15, 12, PedigreeMuted);
        card.Box("进度底槽", 210, y + 6, width, 5, PedigreeBackground);
        if (progress > 0)
        {
            card.Box("进度", 210, y + 6, width * progress, 5, tint);
        }
    }

    private static string BirthPotential(int potential) => potential <= 3 ? CatText.T("偏低") : potential <= 5 ? CatText.T("中等") : CatText.T("偏高");

    private static void AddTraitCards(PedigreeCanvas group, string category, Color tint, params string[] traits)
    {
        group.Text(category, 9, 6, 125, 17, 12, tint);
        for (int index = 0; index < traits.Length; index++)
        {
            int y = 27 + index * 18;
            group.Box("性状标示", 10, y + 5, 3, 6, tint);
            group.Text(traits[index], 20, y, 116, 16, 13, PedigreeInk);
        }
    }

    private void ShowCatScan(CustomUIManager ui, Cat cat, CatGeneScan scan)
    {
        var builder = CustomUIManager.Window(ScanWindowId, CatText.T("测序结果"), CustomUIManager.LAYER_PAPER)
            .SetSize(676, 382)
            .SetDraggable(true)
            .Center();
        var page = PedigreeCanvas.Create(builder, ui, 632, 310);
        page.Text(ClipName(cat.Name, 30), 15, 8, 355, 26, 21, PedigreeInk);
        int mutations = scan.MaternalMutationMasks.Concat(scan.PaternalMutationMasks)
            .Sum(mask => Convert.ToString(mask, 2).Count(bit => bit == '1'));
        if (mutations > 0)
        {
            page.Box("突变图例边框", 370, 14, 14, 16, MutationBorder);
            page.Box("突变图例内底", 372, 16, 10, 12, PedigreeBackground);
            page.Text(CatText.F("本代突变 {0} 处（紫框）", mutations), 392, 12, 230, 19, 13, PedigreeInk);
        }
        else
        {
            string status = cat.MotherId == null ? CatText.T("初代个体，无亲本对照") :
                scan.MaternalMutationMasks.Length == 0 ? CatText.T("无出生突变记录") : CatText.T("本代无突变");
            page.Text(status, 372, 12, 247, 19, 13, PedigreeMuted);
        }
        page.Text(CatText.T("每组为一个位点"), 101, 36, 126, 18, 13, PedigreeMuted);
        page.Badge(CatText.T("左 · 母源"), 228, 33, 78, PedigreeMaternal);
        page.Badge(CatText.T("右 · 父源"), 310, 33, 78, PedigreePaternal);
        page.Text(CatText.T("染色体"), 16, 53, 70, 17, 13, PedigreeMuted);
        const int bitStart = 88;
        const int pairStep = 28;
        for (int bit = 0; bit < scan.Maternal.Select(s => s.Length).DefaultIfEmpty().Max(); bit++)
            page.Text((bit + 1).ToString(), 10 + bitStart + bit * pairStep, 54, 24, 16, 11, PedigreeMuted, center: true);
        string[] groups = { "A", "B", "C", "D", "E", "F", cat.IsMale ? "XY" : "XX" };
        for (int index = 0; index < groups.Length; index++)
        {
            int y = 70 + index * 33;
            var row = page.Section("染色体 " + groups[index], 10, y, 612, 30,
                index % 2 == 0 ? PedigreePanel : PedigreeCard);
            row.Text(groups[index], 5, 6, 73, 18, 14, PedigreeInk, center: true);
            string maternal = index < 6 ? scan.Maternal.ElementAtOrDefault(index) ?? "" : scan.MaternalX;
            string paternal = index < 6 ? scan.Paternal.ElementAtOrDefault(index) ?? "" : scan.PaternalSex;
            int motherMask = scan.MaternalMutationMasks.ElementAtOrDefault(index);
            int fatherMask = scan.PaternalMutationMasks.ElementAtOrDefault(index);
            if (index == 6 && cat.IsMale)
            {
                row.Text(CatText.T("母源 X"), bitStart, 7, 55, 17, 13, PedigreeMaternal);
                for (int bit = 0; bit < maternal.Length; bit++)
                    DrawScanBit(row, maternal[bit], bitStart + 54 + bit * 14, PedigreeMaternal, motherMask, bit);
                int yStart = bitStart + 74 + maternal.Length * 14;
                row.Text(CatText.T("父源 Y"), yStart, 7, 55, 17, 13, PedigreePaternal);
                for (int bit = 0; bit < paternal.Length; bit++)
                    DrawScanBit(row, paternal[bit], yStart + 54 + bit * 14, PedigreePaternal, fatherMask, bit);
                continue;
            }
            for (int bit = 0; bit < maternal.Length; bit++)
                DrawScanBit(row, maternal[bit], bitStart + bit * pairStep, PedigreeMaternal, motherMask, bit);
            for (int bit = 0; bit < paternal.Length; bit++)
                DrawScanBit(row, paternal[bit], bitStart + bit * pairStep + 12, PedigreePaternal, fatherMask, bit);
        }
        builder.Show();
    }

    private static void DrawScanBit(PedigreeCanvas row, char value, int left, Color tint, int mutationMask, int bit)
    {
        bool mutated = (mutationMask & (1 << bit)) != 0;
        if (mutated) row.Box("本代突变", left, 4, 12, 22, MutationBorder);
        int inset = mutated ? 2 : 0;
        row.Box("亲本来源", left + inset, 4 + inset, 12 - 2 * inset, 22 - 2 * inset, tint);
        row.Text(value.ToString(), left, 6, 12, 18, 13, PedigreeBackground, center: true);
    }

    private static string ClipName(string name, int width)
    {
        int occupied = 0;
        int ellipsis = 0;
        for (int index = 0; index < name.Length; index++)
        {
            occupied += name[index] <= 127 ? 1 : 2;
            if (occupied <= width - 2) ellipsis = index + 1;
            if (occupied > width) return name.Substring(0, ellipsis) + "…";
        }
        return name;
    }

    private sealed class PedigreeCanvas
    {
        private readonly CustomUIManager ui;
        private readonly CustomUIWindow window;
        public RectTransform Root { get; }
        public Image Background { get; }

        private PedigreeCanvas(CustomUIManager ui, CustomUIWindow window, RectTransform root, Image image)
        {
            this.ui = ui;
            this.window = window;
            Root = root;
            Background = image;
        }

        public static PedigreeCanvas Create(CustomUIBuilder builder, CustomUIManager ui, float width, float height)
        {
            builder.BeginPanel(width, height, TextAnchor.UpperLeft, padding: 0, spacing: 0);
            RectTransform root = builder.CurrentContainer;
            var layout = root.GetComponent<VerticalLayoutGroup>();
            if (layout != null) layout.enabled = false;
            var plate = root.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            plate.color = PedigreeBackground;
            plate.raycastTarget = false;
            builder.End();
            return new PedigreeCanvas(ui, builder.Handle, root, plate);
        }

        public PedigreeCanvas Section(string name, float x, float y, float width, float height, Color color)
        {
            Image image = Box(name, x, y, width, height, color);
            return new PedigreeCanvas(ui, window, image.rectTransform, image);
        }

        public Image Box(string name, float x, float y, float width, float height, Color color)
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(Image));
            node.transform.SetParent(Root, false);
            var image = node.GetComponent<Image>();
            Place(image.rectTransform, x, y, width, height);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public void Portrait(Sprite? sprite, float x, float y, float width, float height)
        {
            if (sprite == null) return;
            Image image = Box("猫咪立绘", x, y, width, height, Color.white);
            image.sprite = sprite;
            image.preserveAspect = true;
        }

        public void Text(string value, float x, float y, float width, float height, int size, Color color, bool center = false)
        {
            if (string.IsNullOrEmpty(value)) return;
            string safe = value.Replace('<', '‹').Replace('>', '›');
            var label = new CustomUIBuilder(ui, window, Root).AddLabel(center ? $"<align=center>{safe}</align>" : safe).Last;
            label.SetTextColor(color);
            Place(label.Rect, x, y, width, height);
            label.text.fontSizeMax = size;
            label.text.fontSizeMin = Math.Min(size, 8);
            label.text.enableAutoSizing = true;
        }

        public void Badge(string text, float x, float y, float width, Color tint)
        {
            var pill = Section(text, x, y, width, 20, Color.Lerp(PedigreeBackground, tint, 0.22f));
            pill.Text(text, 1, 2, width - 2, 16, 11, tint, center: true);
        }

        public void MakeInteractive(Action click, string? tooltip = null)
        {
            Background.raycastTarget = true;
            var button = Root.gameObject.AddComponent<Button>();
            button.targetGraphic = Background;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.13f, 1.13f, 1.13f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f);
            button.colors = colors;
            button.onClick.AddListener(() => click());
            if (tooltip != null)
            {
                CustomUIElement.Bind(Root.gameObject, null).SetTooltip(tooltip);
            }
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.pivot = rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
