using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeepSpaceCattery;

/// <summary>Evaluates coat markings in the procedural model's rest coordinates.</summary>
internal sealed class CatPortraitCoat
{
    private static readonly Vector3 Ivory = new(.97f, .93f, .84f);
    private static readonly Vector3 PatchScale = new(1.35f, 2f, 1.7f);
    private readonly Dictionary<bool, Color32[]> fieldAtlases = new();

    private readonly Dictionary<bool, Color32[]> calicoAtlases = new();

    public void Clear()
    {
        fieldAtlases.Clear();
        calicoAtlases.Clear();
    }

    public void Apply(LoadedMod mod, Color32[] pixels, int width, int height, bool kitten, int earShapeIndex,
        Phenotype phenotype, Color32 main, Color32 patch)
    {
        if (phenotype.FurLength == "hairless" || phenotype.SuppressedWhite ||
            (phenotype.CoatPattern <= 1 && phenotype.MarkingIndex == 0) ||
            GetFields(mod, kitten, width, height) is not Color32[] fields)
        {
            return;
        }

        float seed = (float)Math.Round(phenotype.PatternSeed * 65535d);
        var shift = new Vector3(Hash(seed, 3, 7), Hash(seed, 8, 2), Hash(seed, 1, 9)) * 31f;
        Vector3 baseColor = ToColor(main);
        Vector3 patchColor = ToColor(patch);
        float density = Unit((float)phenotype.Bands);
        float white = Unit((float)phenotype.White);
        int pattern = phenotype.CoatPattern;
        int marking = phenotype.MarkingIndex;
        var calico = pattern == 4 ? GetCalicoPaint(mod, kitten, width, height) : null;
        int stride = 4 * width;
        int tileRow = 5 - (2 * earShapeIndex + (phenotype.FurLength == "long" ? 1 : 0));
        for (int y = 0; y < height; y++)
        {
            int first = (tileRow * height + y) * stride;
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Color32 original = pixels[index];
                Color32 finish = fields[first + 3 * width + x];
                if (original.a < 8 || finish.a < 8 || finish.g < 2)
                {
                    continue;
                }

                Color32 anatomy = fields[first + x];
                Color32 rest = fields[first + width + x];
                Color32 region = fields[first + 2 * width + x];
                var p = new Vector3(anatomy.r * (2.9f / 255f) - 1.45f, anatomy.g / 255f,
                    anatomy.b * (2.75f / 255f) - 1.70f);
                var islands = new Vector3(rest.r * (3.30f / 255f) - 1.65f,
                    rest.g * (1.32f / 255f) - .16f, rest.b * (3.75f / 255f) - 1.85f);
                int part = (int)MathF.Round(region.r * (3f / 255f));
                float h = region.g / 255f;
                float dorsal = region.b * (1.80f / 255f) - .35f;
                float relief = finish.r * (1.5f / 255f);
                float light = finish.b / 255f;
                var coolShadow = new Vector3(Mix(.77f, 1.04f, light), Mix(.80f, 1.02f, light), Mix(.87f, 1f, light));
                Vector3 pigment;
                if (calico != null)
                {
                    int paintIndex = (tileRow * height + y) * (3 * width) + x;
                    // White follows the painted bib, cheeks and paws. The seed moves those
                    // borders on the same surface coordinates used by the other markings.
                    float wave = Islands(islands, shift) - .5f;
                    float amount = Unit((white - .20f) / .70f + .65f * wave);
                    float level = amount * 2f;
                    int band = level < 1f ? 0 : 1;
                    Vector3 paint = Mix(ToColor(calico[paintIndex + band * width]),
                        ToColor(calico[paintIndex + (band + 1) * width]), level - band);
                    float warm = Smooth(.07f, .29f, paint.x - paint.z) *
                        (1f - Smooth(.64f, .87f, paint.y / MathF.Max(.01f, paint.x)));
                    float ink = 1f - Smooth(.28f, .52f, paint.x);
                    paint += (baseColor - new Vector3(.784f, .467f, .243f)) * (warm * .72f);
                    paint += (patchColor - new Vector3(.251f, .239f, .282f)) * (ink * (1f - warm) * .68f);
                    // The painted guide already includes facet lighting. Remove it here
                    // so the shared marking and shading pass applies it exactly once.
                    pigment = new Vector3(paint.x / MathF.Max(.05f, relief * coolShadow.x),
                        paint.y / MathF.Max(.05f, relief * coolShadow.y),
                        paint.z / MathF.Max(.05f, relief * coolShadow.z));
                }
                else
                {
                    pigment = MainPattern(pattern, p, islands, part, h, dorsal, density, white,
                        shift, baseColor, patchColor);
                }
                pigment = SpecialMarking(marking, pigment, p, islands, part, h, dorsal, density, white,
                    shift, baseColor, patchColor);
                Vector3 shaded = Vector3.Scale(pigment, coolShadow) * relief;
                float coverage = finish.g / 255f;
                pixels[index] = new Color32(
                    Byte(Mix(original.r / 255f, Unit(shaded.x), coverage)),
                    Byte(Mix(original.g / 255f, Unit(shaded.y), coverage)),
                    Byte(Mix(original.b / 255f, Unit(shaded.z), coverage)), original.a);
            }
        }
    }

    private Color32[]? GetCalicoPaint(LoadedMod mod, bool kitten, int width, int height)
    {
        if (calicoAtlases.TryGetValue(kitten, out var pixels))
            return pixels;
        string key = kitten ? "cattery_calico_kitten" : "cattery_calico_adult";
        if (!mod.Sprites.TryGetValue(key, out var sprite) || sprite?.texture is not Texture2D texture ||
            texture.width != 3 * width || texture.height != 6 * height || !texture.isReadable)
            return null;
        return calicoAtlases[kitten] = texture.GetPixels32();
    }

    private Color32[]? GetFields(LoadedMod mod, bool kitten, int width, int height)
    {
        if (fieldAtlases.TryGetValue(kitten, out Color32[]? cached))
        {
            return cached;
        }
        string key = kitten ? "cattery_coat_fields_kitten" : "cattery_coat_fields_adult";
        if (!mod.Sprites.TryGetValue(key, out Sprite? sprite) || sprite?.texture is not Texture2D texture ||
            !texture.isReadable || texture.width != 4 * width || texture.height != 6 * height)
        {
            return null;
        }
        Color32[] data = texture.GetPixels32();
        fieldAtlases.Add(kitten, data);
        return data;
    }

    private static Vector3 MainPattern(int pattern, Vector3 p, Vector3 islands, int part, float h, float dorsal,
        float density, float white, Vector3 shift, Vector3 main, Vector3 patch)
    {
        switch (pattern)
        {
            case 6: // Smoke shows most of its pale undercoat at the throat and flanks.
            case 7: // Shaded and shell have successively lighter tipping.
            case 8:
                float grain = Noise(islands * 13f + shift) - .5f;
                float mantle = Smooth(.12f, .92f, dorsal + grain * .18f);
                float tipping = pattern switch
                {
                    6 => Mix(.45f, .96f, mantle),
                    7 => Mix(.10f, .59f, mantle),
                    _ => Mix(.025f, .24f, mantle)
                };
                return Mix(Ivory, main, tipping);
            case 2:
                float flecks = Noise(Vector3.Scale(p, new Vector3(15f, 24f, 14f)) + shift) - .5f;
                return Mix(main, patch, .86f * Smooth(.13f, .99f, dorsal + .065f * flecks));
            case 3:
                Vector3 bandColor = Mix(main * .45f, patch, .73f);
                return Mix(main, bandColor, .9f * Tabby(p, part, h, density, shift));
            case 4:
            case 5:
                float boundary = pattern == 4 ? .50f : .48f;
                Vector3 colored = Mix(main, patch, Smooth(boundary - .028f, boundary + .028f, Islands(islands, shift)));
                if (pattern == 4)
                {
                    float whiteField = Islands(new Vector3(islands.z, islands.x, islands.y),
                        new Vector3(shift.y + 9f, shift.z + 14f, shift.x + 2f)) + .19f * (.46f - islands.y);
                    float threshold = Mix(.67f, .43f, white);
                    colored = Mix(colored, Ivory, Smooth(threshold - .028f, threshold + .028f, whiteField));
                }
                return colored;
            default:
                return main;
        }
    }

    private static Vector3 SpecialMarking(int marking, Vector3 pigment, Vector3 p, Vector3 islands,
        int part, float h, float dorsal, float density, float white, Vector3 shift, Vector3 main, Vector3 patch)
    {
        switch (marking)
        {
            case 8: // Black coat with four white paws.
            case 1:
            case 2:
            case 3:
                float x = MathF.Abs(p.x);
                float wobble = .024f * (Noise(p * 4.2f + shift) - .5f);
                float cuff = (p.z >= -.1f ? Mix(.13f, .32f, white) : Mix(.045f, .14f, white)) +
                    wobble + .017f * MathF.Sin(3f * p.x + shift.x);
                float glove = part == 2 ? 1f - Smooth(cuff - .023f, cuff + .023f, h) : 0f;
                if (marking == 1 || marking == 8)
                {
                    return Mix(pigment, Ivory, glove);
                }
                if (marking == 2)
                {
                    float snowline = .40f + .17f * (white - .5f) + .043f * (1f - MathF.Min(x, 1f)) + wobble;
                    float snow = part == 3 ? 0f : 1f - Smooth(snowline - .019f, snowline + .019f, dorsal);
                    float facet = .30f * pigment.x + .59f * pigment.y + .11f * pigment.z;
                    Vector3 dark = Mix(new Vector3(.13f, .13f, .16f), new Vector3(.26f, .25f, .30f), facet);
                    return Mix(dark, Ivory, MathF.Max(glove, snow));
                }
                float bibWidth = Mix(.34f, .80f, h) + .16f * (white - .5f);
                float bib = part switch
                {
                    0 => Smooth(.01f, .48f, p.z) * (1f - Smooth(bibWidth - .035f, bibWidth + .035f, x + wobble)) *
                        Smooth(.14f, .43f, h),
                    1 => (1f - Smooth(.18f, .25f, h + wobble)) * (1f - Smooth(.64f, .85f, x)),
                    _ => 0f
                };
                return Mix(pigment, Ivory, bib);
            case 4:
                Vector3 points = Mix(main * .43f, patch * .73f, .75f);
                return Mix(Mix(Ivory, main, .29f), points, PointDepth(p, part, h));
            case 9:
            case 10:
                if (part == 3) return main;
                if (marking == 10 && part == 1)
                {
                    float dx = (p.x - .06f * MathF.Sin(shift.x)) / .29f;
                    float dy = (h - .64f) / .16f;
                    float stamp = (1f - Smooth(.70f, 1.08f, dx * dx + dy * dy)) * Smooth(.28f, .63f, p.z);
                    return Mix(Ivory, main, stamp);
                }
                return Ivory;
            case 5:
            case 6:
                return Mix(Ivory, pigment, HighWhite(p, islands, part, h, shift, white, marking == 5));
            case 7:
                Vector3 ink = Mix(main * .35f, patch * .78f, .68f);
                return Mix(pigment, ink, Spots(p, islands, part, h, density, shift));
            default:
                return pigment;
        }
    }

    private static float PointDepth(Vector3 p, int part, float h)
    {
        if (part == 0) return 0f;
        if (part == 2) return 1f - Smooth(.13f, p.z < -.1f ? .46f : .74f, h);
        if (part == 3) return .84f + .16f * Smooth(0f, .45f, h);
        float x = p.x / .78f, y = (h - .24f) / .31f;
        float mask = (1f - Smooth(.60f, 1.32f, MathF.Sqrt(x * x + y * y))) * Smooth(.58f, .86f, p.z);
        return MathF.Max(mask, Smooth(.51f, .76f, h) * Smooth(.20f, .45f, MathF.Abs(p.x)));
    }

    private static float Tabby(Vector3 p, int part, float h, float density, Vector3 shift)
    {
        float x = MathF.Abs(p.x);
        float curve = .47f * MathF.Sin(p.z * 4.7f + shift.y) + .16f * MathF.Sin(p.z * 10f + shift.z);
        float phase = p.z * Mix(2.25f, 5.85f, density) + curve * (1f - p.y) + .58f * x + .34f * p.y +
            .24f * Noise(p * 3.2f + shift);
        float stripe = Band(phase, .08f + .035f * MathF.Sin(p.z * 3.4f + shift.z));
        switch (part)
        {
            case 0:
                float spine = (1f - Smooth(.035f, .14f, x)) * Smooth(.52f, .78f, h) *
                    (1f - Smooth(-.14f, .20f, p.z));
                float end = Smooth(.06f, .35f, h + .11f * MathF.Sin(p.z * 9f + shift.x));
                return MathF.Max(.87f * spine, stripe * end) * (.38f + .62f * Smooth(.10f, .61f, x + .26f * (.5f - p.z)));
            case 1:
                float center = .13f + .46f * MathF.Abs(h - .63f);
                float forehead = (1f - Smooth(.022f, .075f, MathF.Abs(x - center))) * Smooth(.37f, .53f, h) *
                    (1f - Smooth(.80f, .97f, h));
                float temples = (1f - Smooth(.026f, .085f, MathF.Abs(x - .44f - .32f * (h - .58f)))) *
                    Smooth(.48f, .62f, h) * (1f - Smooth(.79f, .95f, h));
                float cheek = Band(h * 4.6f + x * .71f + p.z * .20f, .095f) * Smooth(.36f, .60f, x) *
                    (1f - Smooth(.45f, .56f, h));
                return MathF.Max(MathF.Max(forehead, .77f * temples), cheek);
            case 2:
                float rings = Band(h * (2.1f + density * 1.85f) + .19f * x + .06f * MathF.Sin(p.z * 8f + shift.z), .087f);
                return Mix(.87f * rings, .64f * stripe, Smooth(.52f, .90f, h)) * Smooth(.035f, .17f, h);
            default:
                return Band(h * (3.2f + 3.8f * density) + .07f * MathF.Sin(p.x * 5f + shift.x), .17f);
        }
    }

    private static float Spots(Vector3 p, Vector3 islands, int part, float h, float density, Vector3 shift)
    {
        if (part == 1 || part == 3) return Tabby(p, part, h, density, shift);
        Vector3 q = Vector3.Scale(islands, new Vector3(3.8f, 8.6f, 4.1f)) * Mix(.86f, 1.27f, density);
        (q.x, q.z) = (.91f * q.x - .41f * q.z, .41f * q.x + .91f * q.z);
        (q.x, q.y) = (.94f * q.x + .34f * q.y, -.34f * q.x + .94f * q.y);
        q += shift + new Vector3(MathF.Sin(q.z * 1.6f), MathF.Sin(q.x * 1.6f), MathF.Sin(q.y * 1.6f)) * .13f;
        Vector3 cell = Floor(q);
        var jitter = new Vector3(Hash(cell), Hash(cell + Vector3.one * 17f), Hash(cell + Vector3.one * 32f));
        Vector3 delta = Fraction(q) - (Vector3.one * .5f + (jitter - Vector3.one * .5f) * .13f);
        float distance = Length(new Vector3(delta.x / Mix(.32f, .43f, jitter.x),
            delta.y / Mix(.29f, .41f, jitter.y), delta.z / Mix(.30f, .43f, jitter.z)));
        float spot = 1f - Smooth(.86f, 1.14f, distance);
        return part == 2 ? Mix(Tabby(p, part, h, density, shift), spot, Smooth(.35f, .73f, h)) : spot;
    }

    private static float HighWhite(Vector3 p, Vector3 islands, int part, float h, Vector3 shift, float white, bool van)
    {
        if (part == 3) return 1f;
        float flip = shift.z > 15.5f ? 1f : -1f;
        float rag = .12f * (Noise(islands * 8f + shift) - .5f);
        if (part == 1)
        {
            float side = p.x * flip;
            if (van)
            {
                float blaze = .075f + .08f * white + .035f * MathF.Sin(h * 7f + shift.x);
                float crown = Smooth(.38f, .46f, h + .08f * side + rag) * (1f - Smooth(.72f, .85f, h + rag));
                return Smooth(blaze - .018f, blaze + .018f, MathF.Abs(side + .035f) + rag) * crown;
            }
            float edge = .09f + .45f * (white - .5f) - .52f * (h - .4f) + rag;
            return Smooth(edge - .025f, edge + .025f, side) * Smooth(.18f, .24f, h + rag);
        }
        if (van) return 0f;
        var q = new Vector3(islands.x * flip, islands.y, islands.z);
        float size = Mix(1.14f, .87f, white);
        Vector3 drift = (Fraction(shift * .63f) - Vector3.one * .5f) * .11f;
        float shoulder = Piebald(q, new Vector3(.54f, .54f, .26f) + drift, new Vector3(.65f, .39f, .39f) * size, rag);
        float flank = Piebald(q, new Vector3(-.62f, .52f, -.25f) - drift, new Vector3(.67f, .45f, .47f) * size, rag);
        float rump = Piebald(q, new Vector3(.24f, .51f, -.69f) - new Vector3(drift.z, drift.y, drift.x),
            new Vector3(.53f, .36f, .33f) * size, rag);
        float color = MathF.Max(shoulder, MathF.Max(flank, rump)) * Smooth(.14f, .30f, islands.y);
        return part == 2 ? color * Smooth(.42f, .75f, h) : color;
    }

    private static float Piebald(Vector3 q, Vector3 center, Vector3 radius, float edge)
    {
        Vector3 delta = q - center;
        float d = Length(new Vector3(delta.x / radius.x, delta.y / radius.y, delta.z / radius.z));
        return 1f - Smooth(.95f, 1.05f, d + edge);
    }

    private static float Islands(Vector3 p, Vector3 shift)
    {
        Vector3 cell = Vector3.Scale(p, PatchScale) + shift;
        var ripple = new Vector3(MathF.Sin(cell.z * 2.1f + cell.y), MathF.Cos(cell.x * 2.4f - cell.z),
            MathF.Sin(cell.y * 2.3f + cell.x));
        float lobes = MathF.Sin(p.x * 2.31f + p.y * 4.65f + p.z * 4.97f + shift.x + 1.5f * ripple.x);
        return .11f + .18f * lobes + .67f * Noise(cell + ripple * .16f) +
            .11f * Noise(cell * 3.6f + new Vector3(3.9f, 1.6f, 7.2f));
    }

    private static float Noise(Vector3 p)
    {
        Vector3 cell = Floor(p);
        Vector3 f = p - cell;
        var t = new Vector3(Curve(f.x), Curve(f.y), Curve(f.z));
        float lower = Plane(cell.x, cell.y, cell.z, t.x, t.y);
        float upper = Plane(cell.x, cell.y, cell.z + 1f, t.x, t.y);
        return Mix(lower, upper, t.z);
    }

    private static float Plane(float x, float y, float z, float tx, float ty) =>
        Mix(Mix(Hash(x, y, z), Hash(x + 1f, y, z), tx),
            Mix(Hash(x, y + 1f, z), Hash(x + 1f, y + 1f, z), tx), ty);

    private static float Hash(Vector3 p) => Hash(p.x, p.y, p.z);

    private static float Hash(float x, float y, float z)
    {
        x = Fract(x * .1031f);
        y = Fract(y * .1030f);
        z = Fract(z * .0973f);
        float d = x * (y + 33.33f) + y * (x + 33.33f) + z * (z + 33.33f);
        return Fract((x + y + 2f * d) * (z + d));
    }

    private static float Band(float phase, float width) =>
        1f - Smooth(width - .07f, width + .07f, MathF.Abs(Fract(phase + .5f) - .5f));

    private static Vector3 ToColor(Color32 color) => new(color.r / 255f, color.g / 255f, color.b / 255f);
    private static Vector3 Floor(Vector3 p) => new(MathF.Floor(p.x), MathF.Floor(p.y), MathF.Floor(p.z));
    private static Vector3 Fraction(Vector3 p) => p - Floor(p);
    private static float Length(Vector3 p) => MathF.Sqrt(p.x * p.x + p.y * p.y + p.z * p.z);
    private static float Fract(float value) => value - MathF.Floor(value);
    private static float Unit(float value) => Math.Clamp(value, 0f, 1f);
    private static float Curve(float value) => value * value * (3f - 2f * value);
    private static float Smooth(float a, float b, float value) => Curve(Unit((value - a) / (b - a)));
    private static float Mix(float a, float b, float amount) => a + (b - a) * amount;
    private static Vector3 Mix(Vector3 a, Vector3 b, float amount) => a + (b - a) * amount;
    private static byte Byte(float value) => (byte)MathF.Round(Unit(value) * 255f);
}
