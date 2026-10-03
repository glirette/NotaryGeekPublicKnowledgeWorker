using NotaryGeek.PublicKnowledge.Worker.Models;

namespace NotaryGeek.PublicKnowledge.Worker.Services;

// Compile-only copy of the storage record signature: no storage service is
// instantiated, and none of the exercised methods uses this type. This keeps
// the focused offline harness independent of Azure packages and credentials.
public sealed record PublicKnowledgeStoredRunEnvelope(
    string Schema,
    string Version,
    DateTime StoredAtUtc,
    string Trigger,
    string Batch,
    string CaseId,
    string BlobName,
    string LatestBlobName,
    PublicKnowledgeRunResult Result);
