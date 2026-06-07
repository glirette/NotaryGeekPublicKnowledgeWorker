namespace NotaryGeek.PublicKnowledge.Worker.Configuration;

public sealed class PublicKnowledgeOptions
{
    public bool Enabled { get; set; }
    public bool TimerEnabled { get; set; }
    public string PublicBaseUrl { get; set; } = "https://notary.cx";
    public string PublicCorpusManifestUrl { get; set; } = string.Empty;
    public string LocalManifestPath { get; set; } = "public-knowledge/public-knowledge-manifest.json";
    public string LocalRegressionMatrixPath { get; set; } = "public-knowledge/public-knowledge-regression-matrix.json";
    public string AllowedSourceHosts { get; set; } =
        "notary.cx;www.notary.cx;raw.githubusercontent.com;github.com;www.hcch.net;travel.state.gov;dos.fl.gov;dos.myflorida.com;dos.ny.gov;www.sos.state.tx.us;law.justia.com;leginfo.legislature.ca.gov";
    public string DefaultSourcePaths { get; set; } =
        "/notarial-routing-model.json;/source-quality-routing-layer.json;/authority-topics.json;/content-index.json;/source-archive/index.json;/notary-law-sources.json;/law-source-cache/source-cache-manifest.json";
    public int MaxSourcesPerRun { get; set; } = 24;
    public int SourceFetchConcurrency { get; set; } = 12;
    public int MaxBytesPerSource { get; set; } = 750_000;
    public int MaxCharactersPerSource { get; set; } = 20_000;
    public int MaxInputCharacters { get; set; } = 60_000;
    public int MaxEstimatedInputTokens { get; set; } = 18_000;
    public int MaxOutputTokens { get; set; } = 5_000;
    public string UserAgent { get; set; } = "NotaryGeekPublicKnowledgeWorker/0.1";
}
