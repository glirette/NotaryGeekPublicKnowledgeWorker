using NotaryGeek.PublicKnowledge.Worker.Models;

namespace NotaryGeek.PublicKnowledge.Worker.Services;

public static class PublicKnowledgeExecutionPolicy
{
    public static bool ShouldCallProvider(string trigger, string runKind, bool executeRequested)
    {
        if (!executeRequested)
        {
            return false;
        }

        // The pump is a health/index refresh lane. This also neutralizes legacy
        // pump envelopes that were queued before the lanes were separated.
        return !trigger.Equals("pump-timer", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFreshAuthorityRun(
        PublicKnowledgeStoredRunEnvelope? latest,
        string expectedBatch,
        string expectedLane,
        DateTime freshAfterUtc) =>
        latest is not null &&
        latest.Result.Ok &&
        latest.Result.StructuredOutput is not null &&
        latest.Result.RunKind.Equals("authority-generation", StringComparison.OrdinalIgnoreCase) &&
        latest.Result.AuthorityLane.Equals(expectedLane, StringComparison.OrdinalIgnoreCase) &&
        latest.Batch.Equals(expectedBatch, StringComparison.OrdinalIgnoreCase) &&
        latest.StoredAtUtc >= freshAfterUtc;

    public static int SelectOutputTokenBudget(
        int defaultMaxOutputTokens,
        int authorityMaxOutputTokens,
        int repairMaxOutputTokens,
        int highCostMaxOutputTokens,
        bool authorityRun,
        bool repairAttempt,
        bool highCostGuardActive)
    {
        var selected = authorityRun
            ? Math.Max(defaultMaxOutputTokens, authorityMaxOutputTokens)
            : defaultMaxOutputTokens;
        if (repairAttempt)
        {
            selected = Math.Max(selected, repairMaxOutputTokens);
        }

        return highCostGuardActive
            ? Math.Min(selected, Math.Max(1, highCostMaxOutputTokens))
            : selected;
    }

    public static string CreateDailyAuthorityJobId(string batch, DateTime utcDate)
    {
        var safeBatch = new string(batch
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray());
        return $"authority-{utcDate:yyyyMMdd}-{safeBatch}";
    }
}
