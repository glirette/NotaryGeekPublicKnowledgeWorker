# KBA research — source, meaning and publication review

October 30, 2026 · Public research draft for Greg’s customer-guidance review.

Reviewed research source: [`db03e29aa4b6376a9fc6855b690c07effbc7f392`](https://github.com/glirette/NotaryGeekPublicKnowledgeWorker/commit/db03e29aa4b6376a9fc6855b690c07effbc7f392).
Source tree: `1922caeac3901d8a103cc99fdaf603cb5d04d14c`.
The published source tree and all seven added blob identities match the independently reviewed local candidate. This review note is an additive handoff; it does not change those seven source files.

## Result

A separate read-only reviewer inspected all seven files and independently checked the primary-source meanings. No blocking source, meaning or public-boundary issue was found. This is independent research review, not an external legal opinion, certification, transaction qualification or Greg’s customer-guidance approval.

The reviewed corpus has 22 source records, 24 findings, seven proposed correction/routing rules and 22 synthetic semantic examples. Four contrary interpretations are expressly tested, including lawful Virginia KBA combinations, useful supplemental screening, conditional Proof alternatives and Virginia validation law.

The reviewer’s two minor suggestions were incorporated before final review: optional commercial shared-secret/customer-data questions retain their enrollment restrictions, and routing references use `claimIds` to avoid confusing claim references with primary source IDs.

## Independently checked source meanings

- [NIST A-4](https://pages.nist.gov/800-63-4/sp800-63a.html): verification and fraud-management roles remain distinct.
- [Chapter 832](https://legacylis.virginia.gov/cgi-bin/legp604.exe?241+ful+CHAP0832+pdf=) and [current Virginia law](https://law.lis.virginia.gov/vacode/title47.1/chapter1/section47.1-2/): KBA insertion, retrospective validation and actual method combination remain separate.
- [Proof KBA](https://support.proof.com/hc/en-us/articles/10468298021271-Knowledge-Based-Authentication), [failure guide](https://support.proof.com/hc/en-us/articles/42960658744983-Identity-Verification-Failed-Here-s-What-to-Do), [no-SSN](https://support.proof.com/hc/en-us/articles/36549115702295-Verify-Your-Identity-Without-a-Social-Security-Number) and [overview](https://support.proof.com/hc/en-us/articles/31945915880343-Identity-Verification-Overview): scoring conflict and conditional alternatives are supported; no live product result is inferred.
- [GAO](https://www.gao.gov/products/gao-19-288) and [LexisNexis](https://risk.lexisnexis.com/products/instantid-q-and-a): breach-driven risk and the proprietary-data counterargument are supported without identifying Proof’s supplier.
- [Virginia records](https://law.lis.virginia.gov/vacode/47.1-14/), [validation](https://law.lis.virginia.gov/vacode/47.1-20.1/) and [Proof Verify](https://support.proof.com/hc/en-us/articles/28519849379991-Verify-Transactions-Overview): required records, validity and separate agent-product scope remain visible.

## Executed validation

Both author and independent reviewer ran `python3 tools/KbaResearchValidation/validate.py`: passed source/claim/rule references, required semantic coverage, source-role separation, empty empirical measurements, discovery draft status, local Markdown links and whitespace.

Six in-memory mutations were rejected: missing source; technical standard promoted to law; invented failure rate; removed U.S.-address regression; missing rule; discovery activation claim.

Both ran `git diff --check`: passed. Both used the unchanged publication checker from public #39 at `5deafd015029752b8619e9f9e7cae55151516f18` over the seven-file delta: zero findings. Manual review found no customer examples, private strategy, identity records or internal-only repository references. The checker is a heuristic, not proof of source accuracy or complete secret detection.

No full application build was run: only research data/prose and isolated Python validation were added. Semantic examples were checked for integrity and coverage; no live-model answer evaluation was performed.

## Remaining boundaries

No transactions, measured failure rates, demonstrated exploit, complete 2026 amendment comparison, specific adopted authority for a particular no-KBA session, or recipient acceptance evidence were obtained. Mutable pages require refresh before customer publication. Exact statutory mapping of a particular Proof session remains unresolved.

[Human explanation](kba-explanation-2026-10-30.md), [matrix](kba-claim-source-matrix-2026-10-30.json), [routing/examples](kba-routing-proposal-2026-10-30.json) and [discovery proposal](kba-discovery-delta-proposal-2026-10-30.json) are ready for Greg’s meaning review. Existing offerings are separated from service possibilities and promises.

All changes stay within new `research/kba-*` files and `tools/KbaResearchValidation/`. Existing California, Virginia, Proof-audit and publication-checker lanes are unchanged. Shared manifests, indexes, existing articles and runtime code are unchanged. No merge, website activation or live provider transaction occurred.
