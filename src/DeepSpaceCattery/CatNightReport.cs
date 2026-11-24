using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceCattery;

internal sealed class CatNightReport
{
    private readonly List<(string? CatId, string Name, string Action)> events = new();

    public void Add(Cat cat, string action) => Add(cat.Id, cat.Name, action);

    public void Add(string? catId, string name, string action) => events.Add((catId, name, action));

    public IEnumerable<string> Lines() => events.GroupBy(entry => entry.CatId)
        .Select(cat => CatText.F("{0}{1}", cat.First().Name,
            string.Join(CatText.T("随后"), cat.Select(entry => entry.Action))));
}
