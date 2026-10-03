# Source retrieval contract explorer

This suite checks issue #26's source trust and citation identity contract. It is a bounded synthetic explorer, not a native HTTP, DNS, TLS, or live-provider test.

## Production seam

`Program.cs` retains its default client registration and delegates only its two named source registrations to `SourceHttpClientRegistration.AddPublicKnowledgeSourceHttpClients`. The helper still creates `HttpClientHandler` with `AllowAutoRedirect=false` for research and source-index clients.

`SyntheticSources` uses the real dependency-injection registration and `IHttpClientFactory`. Its builder filter first invokes the production builder, checks the actual handler type and redirect property, disposes that native handler, and only then substitutes in-memory transport. Default, OpenAI, and Straico clients are construction-only controls; any attempted send on those names fails. Thus these tests establish registration properties and production consumer name selection, not how a native handler behaves on a wire.

## Independent contract

`ContractOracle` never calls production validation or normalization. It uses the platform URI parser to implement the documented contract separately. Citation tests also compare against 22 hand-authored identity labels, so an oracle/parser mistake cannot silently redefine those expectations. URI parsing itself remains a shared platform dependency, not an independently implemented RFC parser.

| Surface | Contract and boundary |
| --- | --- |
| Source admission | Absolute, well-formed HTTPS URL; configured host match ignoring case; no userinfo or nonempty fragment. No new global port or DNS policy. |
| IDN admission | Preserve existing `Uri.Host`/configured spelling behavior. Unicode and punycode allowlist spelling are characterized separately. Citation authority equivalence uses IDN form. |
| Redirects | Follow 301, 302, 303, 307, 308; resolve relative/network-path targets; validate before each send; allow five redirects, reject a sixth. Other statuses are terminal to this helper. |
| Location | Exactly one nonempty, non-whitespace, resolvable value. Read nonvalidated header values before typed parsing can escape whitespace into a path. Nonempty whitespace-bearing locations retain platform URI resolution behavior; this change does not introduce a trimming policy. |
| Loop identity | Scheme/authority equivalence with case-sensitive escaped path/query; do not mistake case-distinct resources for loops. |
| Citation identity | Authority case/default HTTPS port/IDN aliases may match; preserve path case, trailing slash, query case/order, escaped separators and punctuation. Platform canonicalization of dot segments and unreserved escapes remains in effect. Citation fragments address the fetched document and retain the existing fragment-insensitive citation behavior. |
| Source selection | Deduplicate validated identities before the source cap. Preserve path/query case; keep inadmissible spellings in a separate key space so they cannot shadow admitted sources. |
| Batch preparation | Reuse prepared sources only when requested URL sequences are ordinal-equal. Equivalent but differently spelled batches may fetch separately, preserving each original URL. |
| Provenance | Research and remote manifests, law health and cache status keep original/final URLs. Structured citation admission uses fetched final identities, not original redirect aliases. |
| Completion | Cancellation propagates; a failed or interrupted body is not an admitted fetched source. Law health is explicitly a header-only reachability check, not body completeness or citation authority. |

## Bounds and controls

`Grammar.Enumerate` covers 5 statuses × 27 Location shapes; 5 statuses × lengths 0–7; 22 × 22 resource transitions; and 9 terminal statuses. `Grammar.Seeded` adds 512 xorshift32 recipes at seed `0x20C17E`, each with 0–8 steps. Total: 1,180 recipes. The hop cap bounds actual sends to six. Citation admission has 484 ordered pairs through each of two production admission paths. Validation has 14 explicit cases. Sixteen concurrent requests rendezvous at a deterministic gate, then traverse the same final resource without sharing visited state.

Consumer fixtures exercise pre-send, between-hop, pending asynchronous send, and body cancellation; transport failures; throwing and truncated bodies; response disposal; cancellation followed by a fresh request; successful original/final provenance through all four consumers; header-only law health; and source-cap/IDN-shadow regressions. Providers are never executed.

`Reducer.Steps` replays each deletion and produces a step-deletion-minimal failing recipe. `Reducer.Pair` tries coupled then single-character deletions while preserving allowed, distinct identities, query classification where applicable, and the exact observed production failure signature. Neither reducer claims a globally shortest URI. Controls distinguish a legitimate policy refusal from a production/oracle mismatch.

Unexplored classes include arbitrary Unicode/IDNA implementations, every URI grammar production, OS proxy/certificate/cookie/DNS settings, native malformed-header parsing, all HTTP framing faults, HTTP/2 or HTTP/3, arbitrary scheduler interleavings and process-crash recovery. Restart evidence here means cancelled requests followed by new requests, not process-kill persistence. W08 and PR #29 integration remain separate gates.

## Running

With the repository's existing dependencies restored, run its normal build and tests in an environment that independently enforces external-network denial while permitting only isolated VSTest local communication. No project/package changes are needed: the new C# files are automatically included in the existing test project.

A package-free fallback is included for environments without those dependencies:

```bash
python NotaryGeek.PublicKnowledge.Worker.Tests/W20SourceContracts/run_direct.py \
  --dotnet /absolute/path/to/trusted/dotnet \
  --out /absolute/path/to/new-disposable-output
```

This Linux-only fallback requires a trusted installed .NET 10 SDK, Python, and `libseccomp.so.2`. It clears ambient environment variables; installs an inherited socket-denial filter in the parent before any child; verifies denial of IPv4, IPv6 and Unix sockets in parent and exec-child; closes inherited child descriptors; disables package sources, diagnostics and build servers; bounds each child to 120 seconds; and reaps its process group. It refuses to run if its isolation checks fail. It does not download dependencies or start a host. Output must be outside the checkout and new.

The fallback copies relevant production source bytes, constructs the same DI helper, and invokes 23 existing redirect cases plus the new test methods using an explicit small assertion shim. It extracts only a record declaration needed to compile an unchanged policy dependency; storage behavior is not compiled or executed. It uses the SDK's ASP.NET shared framework rather than the repository's unresolved package graph. It is **direct-method execution, not VSTest, not a full application build, and not proof that Program's complete host starts**. Logs and source bindings include source, runner/templates, public JSON fixture inputs and SDK executable hash.

For sensitivity controls, supply one `--mutant` at a time with a fresh output directory:

```text
per-hop              Check the original URI instead of each current/target URI
auto-redirect       Enable automatic redirects on both constructed source handlers
wrong-name          Select OpenAI instead of the research source-client name
collapsed-identity  Lowercase the complete structured citation identity
original-selection  Restore case-insensitive source selection and batch reuse
original-location   Restore typed Location enumeration
```

Each mutant changes only disposable copied source; it must compile and exit nonzero because an actual test fails. A failing mutant establishes sensitivity, not a defect in the current production source. The two `original-*` variants replay the starting source's reproduced behavior.
