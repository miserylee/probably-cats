using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace DeepSpaceCattery;

internal static class CatText
{
    internal static Func<string>? GetLocale;
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new();

    internal static string Locale
    {
        get
        {
            string code = (GetLocale?.Invoke() ?? "zh").ToLowerInvariant();
            if (code.StartsWith("pt", StringComparison.Ordinal)) return "pt";
            return code.Length >= 2 ? code.Substring(0, 2) : "en";
        }
    }

    internal static string T(string source)
    {
        string locale = Locale;
        if (locale == "zh" || source.Length == 0) return source;
        if (!Catalogs.TryGetValue(locale, out var catalog))
        {
            var assembly = typeof(CatText).Assembly;
            using var stream = assembly.GetManifestResourceStream($"Cattery.Languages.{locale}.json")
                ?? assembly.GetManifestResourceStream("Cattery.Languages.en.json")
                ?? throw new InvalidDataException("Missing English cattery catalog");
            using var reader = new StreamReader(stream);
            catalog = JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd())!;
            Catalogs[locale] = catalog;
        }
        return catalog.TryGetValue(source, out string value) ? value : source;
    }

    internal static string F(string source, params object?[] args) => string.Format(T(source), args);
}
