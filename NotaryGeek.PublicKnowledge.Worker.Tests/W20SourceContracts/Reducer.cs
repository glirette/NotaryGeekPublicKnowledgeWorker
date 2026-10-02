namespace NotaryGeek.PublicKnowledge.Worker.Tests.W20SourceContracts;

internal static class Reducer
{
    // Deterministic deletion reducer. Each accepted candidate is replayed against production.
    // 1-minimal for deletion of a step; not a claim of globally shortest URI syntax.
    public static async Task<Recipe> Steps(Recipe input, Func<Recipe, Task<bool>> stillFails)
    {
        var current = input;
        for (int i = 0; i < current.Steps.Length;)
        {
            var candidate = current with { Steps = current.Steps.Where((_, n) => n != i).ToArray() };
            if (await stillFails(candidate)) { current = candidate; i = 0; } else i++;
        }
        return current;
    }

    // Pair reduction preserves valid absolute source identities and the SAME failure mechanism.
    public static async Task<string[]> Pair(string[] input, Func<string[], Task<bool>> stillFails)
    {
        var current = input.ToArray();
        bool query = new Uri(input[0]).Query.Length > 0;
        bool Eligible(string[] pair) => pair.All(ContractOracleAllows) &&
            (!query || pair.All(s => new Uri(s).Query.Length > 0)) && !ContractOracle.SameResource(pair[0], pair[1]);
        bool ContractOracleAllows(string s) => ContractOracle.Allows(s);
        bool changed;
        do
        {
            changed = false;
            // Coupled deletions are necessary for case-only pairs: deleting on one side alone
            // would remove the bug before common context can be reduced.
            for (int n = current[0].IndexOf('/', "https://".Length) + 1; n < Math.Min(current[0].Length, current[1].Length); n++)
            {
                var candidate = current.Select(s => s.Remove(n, 1)).ToArray();
                if (Eligible(candidate) && await stillFails(candidate)) { current = candidate; changed = true; break; }
            }
            for (int side = 0; side < 2 && !changed; side++)
            for (int n = current[side].IndexOf('/', "https://".Length) + 1; n < current[side].Length; n++)
            {
                var candidate = current.ToArray(); candidate[side] = candidate[side].Remove(n, 1);
                if (!Eligible(candidate)) continue;
                if (await stillFails(candidate)) { current = candidate; changed = true; break; }
            }
        } while (changed);
        return current;
    }
}
