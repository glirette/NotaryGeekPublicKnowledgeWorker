namespace NotaryGeek.PublicKnowledge.Worker.Tests.W20SourceContracts;

internal static class Grammar
{
    public static readonly int[] RedirectStatuses = [301, 302, 303, 307, 308];
    public static readonly string[][] Locations = [
        [], [""], ["   "], ["http://["], ["/next", "/other"],
        ["/next"], ["../next"], ["//second.example/next"], ["HTTPS://SOURCE.EXAMPLE:443/next"],
        ["https://source.example:8443/next"], ["https://blocked.example/no"], ["http://source.example/no"],
        ["https://u:p@source.example/no"], ["/next#part"], ["/a%2Fb"], ["/a/../next"],
        ["/Doc"], ["/doc/"], ["/doc?Q=a&q=B"], ["/doc?q=B&Q=a"], ["/start"],
        ["https://bücher.example/next"], ["https://xn--bcher-kva.example/next"], [" /next "],
        ["/next?value=%26%3D%2F"], ["/next?value=+"], ["/next?value=%20"]
    ];

    // Identity labels are hand-authored; no production normalizer determines expectations.
    public static readonly (string Url, string Identity)[] Resources = [
        ("https://source.example/doc", "doc"), ("HTTPS://SOURCE.EXAMPLE:443/doc", "doc"),
        ("https://source.example:8443/doc", "port-doc"),
        ("https://source.example/Doc", "Doc"), ("https://source.example/doc/", "doc/"),
        ("https://source.example/a/../doc", "doc"), ("https://source.example/%64oc", "doc"),
        ("https://source.example/a%2Fb", "escaped-slash"), ("https://source.example/a/b", "slash"),
        ("https://source.example/doc?q=A", "q=A"), ("https://source.example/doc?q=a", "q=a"),
        ("https://source.example/doc?Q=a", "Q=a"), ("https://source.example/doc?q=a&b=2", "q-a-b"),
        ("https://source.example/doc?b=2&q=a", "b-q-a"),
        ("https://source.example/doc?q=+", "plus"), ("https://source.example/doc?q=%20", "space"),
        ("https://source.example/doc?q=%26", "escaped-and"), ("https://source.example/doc?q=&", "and"),
        ("https://source.example/doc.", "doc."), ("https://source.example/doc#part", "doc"),
        ("https://bücher.example/doc", "idn-doc"), ("https://xn--bcher-kva.example/doc", "idn-doc")
    ];

    public static IEnumerable<Recipe> Enumerate()
    {
        foreach (var status in RedirectStatuses)
        foreach (var locations in Locations)
            yield return new(ContractOracle.Start, [new(status, locations)]);
        foreach (var status in RedirectStatuses)
        for (int length = 0; length <= 7; length++)
            yield return new(ContractOracle.Start, Enumerable.Range(1, length)
                .Select(i => new Step(status, [$"/hop{i}"])).ToArray());
        foreach (var start in Resources)
        foreach (var target in Resources)
            yield return new(start.Url, [new(302, [target.Url])]);
        foreach (var terminal in new[] { 200, 204, 300, 304, 305, 306, 400, 404, 500 })
            yield return new(ContractOracle.Start, [new(terminal, ["https://blocked.example/ignored"])]);
    }

    public static IEnumerable<Recipe> Seeded(uint seed, int count)
    {
        // Specified xorshift32, independent of System.Random/runtime version.
        uint state = seed;
        uint Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
        for (int i = 0; i < count; i++)
        {
            var steps = new Step[Next() % 9];
            for (int j = 0; j < steps.Length; j++)
                steps[j] = new(RedirectStatuses[Next() % 5], Next() % 2 == 0
                    ? [$"/hop{j}?n={Next() % 4}"] : Locations[Next() % (uint)Locations.Length]);
            yield return new(ContractOracle.Start, steps);
        }
    }
}
