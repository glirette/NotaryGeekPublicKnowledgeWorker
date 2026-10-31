# KBA claim/source matrix — October 30, 2026

Public research draft for Greg’s meaning review. The [JSON companion](kba-claim-source-matrix-2026-10-30.json) retains each full finding, source locator, evidence limit and proposed correction. These rows are an index. No empirical failure or attack-success rate was measured.

| ID | Evidence status | Layer | Subject | Sources |
|---|---|---|---|---|
| K01 | documented-requirement | technical-assurance | Verification vs fraud-management role | [nist-a4](https://pages.nist.gov/800-63-4/sp800-63a.html); [nist-final](https://csrc.nist.gov/pubs/sp/800/63/4/final) |
| K02 | documented-risk | technical-assurance | Breach-driven threat | [gao](https://www.gao.gov/products/gao-19-288) |
| K03 | attributed-product-claim | provider-configuration | Commercial question-engine inputs | [ln-qa](https://risk.lexisnexis.com/products/instantid-q-and-a) |
| K04 | attributed-product-claim | provider-configuration | Proof credit-history wording | [proof-kba](https://support.proof.com/hc/en-us/articles/10468298021271-Knowledge-Based-Authentication) |
| K05 | documented-limitation | provider-configuration | No-question generation | [proof-kba](https://support.proof.com/hc/en-us/articles/10468298021271-Knowledge-Based-Authentication) |
| K06 | attributed-product-claim | provider-configuration | Thresholds and retry instructions | [proof-kba](https://support.proof.com/hc/en-us/articles/10468298021271-Knowledge-Based-Authentication) |
| K07 | editorial-inference | technical-assurance | Bounded retry risk | [proof-kba](https://support.proof.com/hc/en-us/articles/10468298021271-Knowledge-Based-Authentication); [gao](https://www.gao.gov/products/gao-19-288) |
| K08 | source-conflict | provider-configuration | Scoring conflict | [proof-failed](https://support.proof.com/hc/en-us/articles/42960658744983-Identity-Verification-Failed-Here-s-What-to-Do); [proof-kba](https://support.proof.com/hc/en-us/articles/10468298021271-Knowledge-Based-Authentication) |
| K09 | attributed-product-claim | provider-configuration | Conditional no-SSN route | [proof-no-ssn](https://support.proof.com/hc/en-us/articles/36549115702295-Verify-Your-Identity-Without-a-Social-Security-Number) |
| K10 | documented-limitation | provider-configuration | Settings and fallback | [proof-overview](https://support.proof.com/hc/en-us/articles/31945915880343-Identity-Verification-Overview); [proof-kba](https://support.proof.com/hc/en-us/articles/10468298021271-Knowledge-Based-Authentication) |
| K11 | attributed-product-claim | provider-configuration | Passport vs completed proofing | [proof-id](https://support.proof.com/hc/en-us/articles/360057120014-Acceptable-Forms-of-ID-for-Online-Notarization) |
| K12 | documented-law | legal-permission | Pre-KBA Virginia text | [va-2021](https://legacylis.virginia.gov/cgi-bin/legp604.exe?212+ful+CHAP0078+pdf=) |
| K13 | documented-law | legal-permission | KBA addition/retroactivity distinction | [va-2024](https://legacylis.virginia.gov/cgi-bin/legp604.exe?241+ful+CHAP0832+pdf=); [va-effective](https://law.lis.virginia.gov/constitution/article4/section13/) |
| K14 | documented-law | legal-permission | Current permission | [va-current](https://law.lis.virginia.gov/vacode/title47.1/chapter1/section47.1-2/) |
| K15 | documented-law | legal-permission | Signer certificate/PIV | [va-current](https://law.lis.virginia.gov/vacode/title47.1/chapter1/section47.1-2/) |
| K16 | unresolved | legal-permission | Missing method mapping | [va-current](https://law.lis.virginia.gov/vacode/title47.1/chapter1/section47.1-2/); [va-guidance](https://law.lis.virginia.gov/vacode/2.2-436/); [proof-no-ssn](https://support.proof.com/hc/en-us/articles/36549115702295-Verify-Your-Identity-Without-a-Social-Security-Number) |
| K17 | documented-law | legal-permission | Required record vs extra evidence | [va-record](https://law.lis.virginia.gov/vacode/47.1-14/) |
| K18 | documented-law | legal-permission | Validity and remedies | [va-validation](https://law.lis.virginia.gov/vacode/47.1-20.1/) |
| K19 | editorial-inference | recipient-requirements | Recipient acceptance | [ng-proof](https://notarygeek.net/proof-kba-foreign-signer-escrow.html); [proof-failed](https://support.proof.com/hc/en-us/articles/42960658744983-Identity-Verification-Failed-Here-s-What-to-Do) |
| K20 | documented-product-scope | provider-configuration | Separate Verify product | [proof-verify](https://support.proof.com/hc/en-us/articles/28519849379991-Verify-Transactions-Overview) |
| K21 | publisher-stated-offering | service-boundary | Existing offering | [ng-no-ssn](https://notarygeek.net/no-ssn-required-online-notary.html); [ng-passport](https://notarygeek.net/supported-passports-by-notary-geek-for-people-not-inside-the-united-states.html) |
| K22 | editorial-correction | source-quality | Qualification of data-source shorthand | [ng-kba](https://notarygeek.net/ron-kba-public-record-proprietary-record.html); [proof-kba](https://support.proof.com/hc/en-us/articles/10468298021271-Knowledge-Based-Authentication); [ln-qa](https://risk.lexisnexis.com/products/instantid-q-and-a) |
| K23 | editorial-inference | technical-assurance | Alternative assessment | [gao](https://www.gao.gov/products/gao-19-288); [nist-a4](https://pages.nist.gov/800-63-4/sp800-63a.html) |
| K24 | source-distinction | provider-configuration | Once-only vs statutory reuse ceiling | [proof-kba](https://support.proof.com/hc/en-us/articles/10468298021271-Knowledge-Based-Authentication); [va-current](https://law.lis.virginia.gov/vacode/title47.1/chapter1/section47.1-2/) |

## Source freshness and preservation

- Original notary.cx HTML/JSON endpoints were inaccessible through retrieval; authored explanations were read at corresponding notarygeek.net paths.
- 2026 Chapters 364/365 links returned JavaScript shells. Current Code was read directly; no complete 2026 amendment comparison claimed.
- Chapter 832 plain endpoint initially returned database unavailable; +pdf= returned enacted text. No full forensic capture claimed.
- No dated adopted §2.2-436 authority for the specific no-KBA flow was established. Missing mapping evidence does not establish no such authority exists.
- Mutable sources require refresh before customer publication; displayed update times are provider metadata, not proven version histories.

## Strongest contrary readings

**C01. Dynamic proprietary questions, limits and additional checks can be useful layered fraud controls.** Yes; data may be difficult to obtain and NIST permits fraud-management KBV. This does not establish A-4 identity verification or measured product effectiveness.

**C02. A properly performed Virginia KBA combination can be lawful despite NIST.** Yes; statutory permission and technical assurance answer different questions. Require dated combination, care and records; permission does not rank methods.

**C03. Proof has no-SSN, personal-knowledge, witness and agent options, so foreign signers are not universally excluded.** Correct; universal exclusion would overreach. Eligibility, address condition, enablement, statutory mapping and recipient acceptance remain specific.

**C04. Validation law can preserve an act despite a duty defect.** Material counter-law, not a cosmetic caveat. No validity/remedies ruling; preservation of an act and proof of compliance differ.
