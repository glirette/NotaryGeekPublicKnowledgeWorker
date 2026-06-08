namespace NotaryGeek.PublicKnowledge.Worker.Configuration;

public sealed class StraicoOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.straico.com";
    public string DefaultChatModel { get; set; } = "openai/gpt-5-mini";
    public int TimeoutSeconds { get; set; } = 90;
}
