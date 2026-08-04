namespace NotaryGeek.PublicKnowledge.Worker.Configuration;

public sealed class PublicKnowledgeOptions
{
    public bool Enabled { get; set; }
    public bool TimerEnabled { get; set; }
    public bool PumpTimerEnabled { get; set; }
    public string TimerBatch { get; set; } = "Core";
    public string TimerBatches { get; set; } = string.Empty;
    public string TimerProvider { get; set; } = "OpenAI";
    public string PumpTimerBatches { get; set; } = string.Empty;
    public string PumpTimerProvider { get; set; } = "OpenAI";
    public string Provider { get; set; } = "OpenAI";
    public int AuthorityFreshnessHours { get; set; } = 20;
    public int PromotionFeedMaxCandidates { get; set; } = 20;
    public int SourceFreshnessDays { get; set; } = 14;
    public int RunHistoryRetentionDays { get; set; } = 90;
    public int JobEnvelopeRetentionDays { get; set; } = 30;
    public string PublicBaseUrl { get; set; } = "https://notary.cx";
    public string PublicCorpusManifestUrl { get; set; } = string.Empty;
    public string LocalManifestPath { get; set; } = "public-knowledge/public-knowledge-manifest.json";
    public string LocalRegressionMatrixPath { get; set; } = "public-knowledge/public-knowledge-regression-matrix.json";
    public string LocalLawSourceIndexPath { get; set; } = "public-knowledge/law/notary-law-source-index.json";
    public string OutputStorageConnectionStringSetting { get; set; } = "AzureWebJobsStorage";
    public string OutputContainerName { get; set; } = "public-knowledge-runs";
    public string QueueName { get; set; } = "public-knowledge-run-jobs";
    public string AllowedSourceHosts { get; set; } =
        "notary.cx;www.notary.cx;raw.githubusercontent.com;github.com;developers.openai.com;learn.microsoft.com;docs.github.com;www.hcch.net;travel.state.gov;dos.fl.gov;dos.myflorida.com;dos.ny.gov;www.sos.state.tx.us;www.sos.texas.gov;law.justia.com;leginfo.legislature.ca.gov;support.proof.com;www.nationalnotary.org;signingprofessionalsworkgroup.org;www.signingprofessionalsworkgroup.org;www.ftc.gov;consumerfinance.gov;www.consumerfinance.gov;www.eeoc.gov;www.flsenate.gov;leg.state.fl.us;www.sos.ca.gov;law.lis.virginia.gov;www.commonwealth.virginia.gov;sos.wyo.gov;www.gsccca.org;apps.gsccca.org;elearn.gsccca.org;www.gabar.org";
    public string DefaultSourcePaths { get; set; } =
        "/notarial-routing-model.json;/source-quality-routing-layer.json;/authority-topics.json;/content-index.json;/source-archive/index.json;/notary-law-sources.json;/law-source-cache/source-cache-manifest.json";
    public int MaxSourcesPerRun { get; set; } = 24;
    public int SourceFetchConcurrency { get; set; } = 12;
    public int MaxBytesPerSource { get; set; } = 750_000;
    public int MaxCharactersPerSource { get; set; } = 20_000;
    public int MaxInputCharacters { get; set; } = 60_000;
    public int MaxEstimatedInputTokens { get; set; } = 18_000;
    public int MaxOutputTokens { get; set; } = 1_600;
    public string UserAgent { get; set; } = "NotaryGeekPublicKnowledgeWorker/0.1";
}
