namespace NotaryGeek.PublicKnowledge.Worker.Tests.W20SourceContracts;

// Independent specification: uses the platform URI parser, never production validators/normalizers.
// The grammar also supplies literal expected identities to avoid relying solely on parser agreement.
internal static class ContractOracle
{
    public const string Hosts = "source.example;second.example;bücher.example";
    public const string Start = "https://source.example/start";
    public static bool Allows(string text, string hosts = Hosts)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var u)) return false;
        return u.Scheme == "https" && u.IsWellFormedOriginalString() &&
            u.UserInfo.Length == 0 && u.Fragment.Length == 0 &&
            hosts.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(h => string.Equals(h, u.Host, StringComparison.OrdinalIgnoreCase));
    }

    public static string Identity(string text)
    {
        var u = new Uri(text);
        // Separately compare authority tuple and escaped resource. Fragments address parts of a
        // fetched document; they are ignored for citation identity, but forbidden for source fetches.
        return string.Join("|", u.Scheme.ToLowerInvariant(), u.IdnHost.ToLowerInvariant(), u.Port,
            u.UserInfo, u.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped));
    }

    public static bool SameResource(string a, string b) => Identity(a) == Identity(b);
    public static bool Redirect(int status) => status is 301 or 302 or 303 or 307 or 308;

    public static Observation Predict(Recipe recipe)
    {
        var sent = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = new Uri(recipe.Start);
        for (int i = 0; ; i++)
        {
            if (!Allows(current.AbsoluteUri)) return new("policy", sent, null);
            if (!seen.Add(Identity(current.AbsoluteUri))) return new("loop", sent, null);
            sent.Add(current.AbsoluteUri);
            var step = i < recipe.Steps.Length ? recipe.Steps[i] : new Step(200, []);
            if (!Redirect(step.Status)) return new("terminal", sent, current.AbsoluteUri);
            if (step.Locations.Length != 1 || string.IsNullOrWhiteSpace(step.Locations[0]) ||
                !Uri.TryCreate(current, step.Locations[0], out var target)) return new("location", sent, null);
            if (!Allows(target.AbsoluteUri)) return new("policy", sent, null);
            if (i >= 5) return new("limit", sent, null);
            current = target;
        }
    }
}

internal sealed record Step(int Status, string[] Locations);
internal sealed record Recipe(string Start, Step[] Steps);
internal sealed record Observation(string Kind, IReadOnlyList<string> Sent, string? Final);
