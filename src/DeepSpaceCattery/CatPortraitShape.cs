using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeepSpaceCattery;

internal sealed class CatPortraitShape
{
    private const int TraitCount = 5;
    private const int FurAndEarCount = 9;
    private const float BakedDose = .30f;
    private const float CodesPerPixel = 6f;
    private const int NeutralCode = 128;
    private readonly Dictionary<bool, Color32[]> fields = new();

    public void Apply(Color32[] pixels, int width, int height, Phenotype phenotype, bool kitten, LoadedMod? mod)
    {
        Span<float> doses = stackalloc float[TraitCount]
        {
            Dose(phenotype.Fullness), Dose(phenotype.Cheeks), Dose(phenotype.Muzzle),
            Dose(phenotype.Eyes), Dose(phenotype.Ears)
        };
        if (doses[0] == 0 && doses[1] == 0 && doses[2] == 0 && doses[3] == 0 && doses[4] == 0)
            return;

        int atlasWidth = width * TraitCount;
        int atlasHeight = height * FurAndEarCount;
        if (!TryReadFields(mod, kitten, atlasWidth, atlasHeight, out Color32[] atlas))
            return;

        int fur = phenotype.FurLength switch { "long" => 1, "hairless" => 2, _ => 0 };
        int ears = phenotype.EarShape switch { "slight" => 1, "folded" => 2, _ => 0 };
        int atlasStart = (FurAndEarCount - 1 - (fur * 3 + ears)) * height * atlasWidth;
        var movement = new Vector2[pixels.Length];
        for (int trait = 0; trait < TraitCount; trait++)
        {
            float dose = doses[trait];
            if (dose == 0)
                continue;
            bool upper = dose > 0;
            float scale = Math.Abs(dose) / CodesPerPixel;
            for (int y = 0; y < height; y++)
            {
                int fieldRow = atlasStart + y * atlasWidth + trait * width;
                int outputRow = y * width;
                for (int x = 0; x < width; x++)
                {
                    // Lower dose uses RG, upper dose BA. Both use the same projected body anchors.
                    Color32 encoded = atlas[fieldRow + x];
                    ref Vector2 offset = ref movement[outputRow + x];
                    offset.x += ((upper ? encoded.b : encoded.r) - NeutralCode) * scale;
                    offset.y += ((upper ? encoded.a : encoded.g) - NeutralCode) * scale;
                }
            }
        }

        var shifted = new Color32[pixels.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                Vector2 start = movement[index];
                if (start.sqrMagnitude < .0001f)
                {
                    shifted[index] = pixels[index];
                    continue;
                }

                // Invert the combined field, then sample once so overlapping traits share one silhouette.
                var source = new Vector2(x, y) - start;
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    Vector2 offset = SampleMovement(movement, width, height, source);
                    Vector2 residual = new Vector2(x, y) - source - offset;
                    if (residual.sqrMagnitude < .0001f)
                        break;
                    source += residual * .75f;
                }
                shifted[index] = SampleColor(pixels, width, height, source);
            }
        }
        Array.Copy(shifted, pixels, pixels.Length);
    }

    public void Clear() => fields.Clear();

    private bool TryReadFields(LoadedMod? mod, bool kitten, int width, int height, out Color32[] result)
    {
        if (fields.TryGetValue(kitten, out var known))
        {
            result = known;
            return true;
        }

        string key = kitten ? "cattery_portrait_kitten_shape" : "cattery_portrait_adult_shape";
        if (mod != null && mod.Sprites.TryGetValue(key, out var sprite) && sprite.texture is Texture2D texture &&
            texture.isReadable && texture.width == width && texture.height == height)
        {
            result = texture.GetPixels32();
            fields.Add(kitten, result);
            return true;
        }

        result = Array.Empty<Color32>();
        return false;
    }

    private static float Dose(double value) => Math.Clamp(((float)value - .5f) / BakedDose, -1f, 1f);

    private static Vector2 SampleMovement(Vector2[] movement, int width, int height, Vector2 source)
    {
        float x = Math.Clamp(source.x, 0, width - 1);
        float y = Math.Clamp(source.y, 0, height - 1);
        int left = (int)x, bottom = (int)y;
        int right = Math.Min(left + 1, width - 1), top = Math.Min(bottom + 1, height - 1);
        return Vector2.Lerp(
            Vector2.Lerp(movement[bottom * width + left], movement[bottom * width + right], x - left),
            Vector2.Lerp(movement[top * width + left], movement[top * width + right], x - left), y - bottom);
    }

    private static Color32 SampleColor(Color32[] pixels, int width, int height, Vector2 source)
    {
        int left = Mathf.FloorToInt(source.x), bottom = Mathf.FloorToInt(source.y);
        float x = source.x - left, y = source.y - bottom;
        Color32 a = ReadColor(pixels, width, height, left, bottom);
        Color32 b = ReadColor(pixels, width, height, left + 1, bottom);
        Color32 c = ReadColor(pixels, width, height, left, bottom + 1);
        Color32 d = ReadColor(pixels, width, height, left + 1, bottom + 1);
        float wa = a.a * (1 - x) * (1 - y), wb = b.a * x * (1 - y);
        float wc = c.a * (1 - x) * y, wd = d.a * x * y;
        float alpha = wa + wb + wc + wd;
        if (alpha < .5f)
            return default;
        return new Color32(
            (byte)Mathf.RoundToInt((a.r * wa + b.r * wb + c.r * wc + d.r * wd) / alpha),
            (byte)Mathf.RoundToInt((a.g * wa + b.g * wb + c.g * wc + d.g * wd) / alpha),
            (byte)Mathf.RoundToInt((a.b * wa + b.b * wb + c.b * wc + d.b * wd) / alpha),
            (byte)Mathf.RoundToInt(alpha));
    }

    private static Color32 ReadColor(Color32[] pixels, int width, int height, int x, int y) =>
        (uint)x < (uint)width && (uint)y < (uint)height ? pixels[y * width + x] : default;
}
