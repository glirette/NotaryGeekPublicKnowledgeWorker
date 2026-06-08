namespace NotaryGeek.PublicKnowledge.Worker.Configuration;

public sealed class OpenAiOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com";
    public string EndpointPath { get; set; } = "/v1/responses";
    public string Model { get; set; } = "gpt-5.5-2026-04-23";
    public string ReasoningEffort { get; set; } = "high";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
}
