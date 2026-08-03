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
}
