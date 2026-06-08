namespace NotaryGeek.PublicKnowledge.Worker.Configuration;

public sealed class OpenAiOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com";
    public string EndpointPath { get; set; } = "/v1/responses";
    public string Model { get; set; } = "gpt-5-mini";
    public string ReasoningEffort { get; set; } = "medium";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
    public bool AllowHighCostMode { get; set; }
    public string HighCostModelMarkers { get; set; } = "gpt-5.5;gpt-5_5";
    public string HighCostReasoningEffort { get; set; } = "medium";
    public int HighCostMaxOutputTokens { get; set; } = 1_600;
}
