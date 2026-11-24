using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceCattery;

internal static class KittenNames
{
    internal static string Pick(string id, IEnumerable<Cat> relatives)
    {
        // Use identity rather than the inheritance RNG so naming cannot change a litter's genes.
        uint seed = 2166136261;
        foreach (char letter in id) seed = unchecked((seed ^ letter) * 16777619);
        string[] names = CatText.T("团子|芝麻|栗子|布丁|小满|阿福|奶盖|月饼|豆花|糯米|墨墨|花卷|泡芙|年糕|铃铛|小麦|星星|柚子|糖豆|桃子|松露|米粒|麦芽|小葵").Split('|');
        var taken = new HashSet<string>(relatives.Select(cat => cat.Name), StringComparer.OrdinalIgnoreCase);
        int first = (int)(seed % names.Length);
        for (int round = 0; ; round++)
        {
            for (int step = 0; step < names.Length; step++)
            {
                string root = names[(first + step) % names.Length];
                string name = round == 0 ? root : round <= names.Length
                    ? CatText.F("{0}·{1}", root, names[(first + step + round) % names.Length])
                    : $"{root} {round - names.Length + 1}";
                if (!taken.Contains(name)) return name;
            }
        }
    }
}
