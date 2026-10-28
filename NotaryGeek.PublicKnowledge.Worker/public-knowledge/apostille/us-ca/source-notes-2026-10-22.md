# California source and route notes — October 22, 2026

This public note supports the [guide](README.md), [machine-readable routes](us-ca-apostille-authentication.json), and [17 adversarial cases](adversarial-cases-2026-10-22.json). It adds to the [September 29 historical ledger](../adversarial-review-2026-09-29.md); that ledger is preserved rather than rewritten. California remains partial.

## Evidence method and dates

Claims are paraphrases with source IDs and locators in `routeFacts`. A checked date is a research observation, not an effective date. Day precision is explicit; midnight timestamps do not claim the fetch happened at midnight. Undated pages have no invented effective date. The HCCH source retains its September 29 check and requires transaction-date verification. No actual certificate, court case, recipient instruction, portal order or agency submission was tested.

| Source ID | Official source | Checked / version | Locator and access limit |
| --- | --- | --- | --- |
| `ca-request` | [Request an Apostille](https://www.sos.ca.gov/notary/request-apostille) — California Secretary of State | 2026-10-22; effective date not stated | See claim-level locator in routeFacts; direct-page-text |
| `ca-faq` | [Apostille Frequently Asked Questions](https://www.sos.ca.gov/notary/apostille-faqs) — California Secretary of State | 2026-10-22; effective date not stated | See claim-level locator in routeFacts; direct-page-text |
| `ca-processing` | [Current Processing Dates](https://www.sos.ca.gov/notary/processing-times) — California Secretary of State | 2026-10-22; 2026-10-22 | See claim-level locator in routeFacts; direct-page-text |
| `ca-la-office` | [Los Angeles Office](https://www.sos.ca.gov/business-programs/los-angeles-office) — California Secretary of State | 2026-10-22; effective date not stated | See claim-level locator in routeFacts; direct-page-text |
| `hcch-status` | [Apostille Convention status table](https://www.hcch.net/en/instruments/conventions/status-table/?cid=41) — Hague Conference on Private International Law | 2026-09-29; effective date not stated | See claim-level locator in routeFacts; retained-source-record; not refreshed in this increment |
| `ca-business-records` | [Business Entities Records Request](https://www.sos.ca.gov/business-programs/business-entities/information-requests) — California Secretary of State | 2026-10-22; effective date not stated | See claim-level locator in routeFacts; direct-page-text |
| `ca-business-order` | [Business Entities Records – Order Form](https://bpd.cdn.sos.ca.gov/pdf/be-records-requests.pdf) — California Secretary of State | 2026-10-22; 03/2026 | Pages 1–3; entity choices, copy/certificate requests, return and Sacramento submission; direct-PDF-text |
| `ca-bizfile-discovery` | [bizfile: Business Records](https://www.sos.ca.gov/business-programs/bizfile) — California Secretary of State | 2026-10-22; effective date not stated | Business Records items 1–4; separate Notary Public and Apostille links; direct-page-text |
| `ca-education-ucdavis` | [Notary Services](https://registrar.ucdavis.edu/records/notary) — UC Davis Office of the University Registrar | 2026-10-22; Page updated 2025-09-08 | Documents Available for Notarization; Delivery; Apostille & Certification Services; direct-page-text |
| `ca-education-berkeley` | [Notary Services](https://www.registrar.berkeley.edu/academic-records/notary-services/) — UC Berkeley Office of the Registrar | 2026-10-22; effective date not stated | Documents eligible for notarization; Apostilles; direct-page-text |
| `ca-education-caltech` | [Apostille](https://registrar.caltech.edu/records/apostille) — Caltech Registrar | 2026-10-22; effective date not stated | Procedure (use for issuer process only; opening true-copy definition is not the SOS authentication rule); direct-page-text |
| `ca-notary-handbook-2026` | [2026 Notary Public Handbook](https://notary.cdn.sos.ca.gov/forms/notary-handbook-current.pdf) — California Secretary of State | 2026-10-22; January 2026; current.pdf is a mutable URL | Printed pp17–19,31–32,44–45; Gov Code 8205(a)(4), 27201.1; direct-PDF-text |
| `ca-cdph-divorce` | [Obtaining Copies of Divorce Records](https://www.cdph.ca.gov/Programs/CHSI/Pages/Vital-Records-Obtaining-Certified-Copies-of-Divorce-Records.aspx) — California Department of Public Health | 2026-10-22; effective date not stated | Opening document/issuer distinction; official-page search-index text; direct retrieval returned 502 |
| `ca-cdc-divorce` | [Where to Write for Vital Records: California](https://www.cdc.gov/nchs/w2w/california.htm) — Centers for Disease Control and Prevention | 2026-10-22; effective date not stated | Divorce / State and County; use for record identity, not its stale fee; direct-page-text |
| `ca-orange-copy-faq` | [Online Family Law Copy Requests: Frequently Asked Questions](https://www.occourts.org/system/files?file=fl_online_copywork_faqs.pdf) — Superior Court of California, County of Orange | 2026-10-22; undated | Page 2 certification/exemplification; page 1 timing conflict; direct PDF text |
| `ca-orange-records` | [Family Law Records](https://www.occourts.org/divisions/family-law/family-law-records) — Superior Court of California, County of Orange | 2026-10-22; effective date not stated | Requesting Copies / Copies of Documents; sealed/confidential access; direct-page-text |
| `ca-stanislaus-g003` | [Certificate of Authentication, G003](https://www.stanislaus.courts.ca.gov/system/files/certificate_authentication.pdf) — Superior Court of California, County of Stanislaus | 2026-10-22; 02/04 | Clerk attestation; judge certificate; clerk certificate; Optional Form; official PDF search-index text; direct retrieval returned 403 |
| `ca-stanislaus-catalog` | [Common Forms & Packets](https://www.stanislaus.courts.ca.gov/forms-filing/common-forms-packets) — Superior Court of California, County of Stanislaus | 2026-10-22; effective date not stated | Civil and Family Law G003 entries; official-page search-index text |
| `ca-business-faq` | [Business Entities Frequently Asked Questions](https://www.sos.ca.gov/business-programs/business-entities/faqs) — California Secretary of State | 2026-10-22; effective date not stated | Electronic Signatures; Digital Signatures; direct-page-text |

## Claim-level evidence map

Use the exact claim in JSON with this map. “Inference” identifies the publisher’s limited route conclusion from agency facts, rather than a quoted agency rule.

| Claim ID | Source IDs | Locator / classification |
| --- | --- | --- |
| `signature-route` | `ca-request`, `ca-faq` | Request page introduction; FAQ 1; official-agency-guidance; checked 2026-10-22 |
| `destination-output` | `ca-faq`, `hcch-status` | FAQ 2; HCCH status table (retained September 29 source date); agency statement plus Notary Geek destination-route inference; checked 2026-10-22 |
| `original-mail` | `ca-request` | How to Request an Apostille by Mail, items 1–4; official-agency-procedure; checked 2026-10-22 |
| `mail-routing` | `ca-request`, `ca-faq` | Mail carrier addresses and return postage; FAQ 7; official-agency-procedure; checked 2026-10-22 |
| `counter-fees` | `ca-request`, `ca-faq` | Sacramento and Los Angeles in-person instructions; FAQ 6; official-agency-procedure; checked 2026-10-22 |
| `vital-signature-exception` | `ca-faq` | FAQ 3; official-agency-document-specific-guidance; checked 2026-10-22 |
| `processing-status` | `ca-processing` | Apostilles / Mail / In Person; last updated October 22, 2026; official-agency-dated-operational-status; checked 2026-10-22 |
| `los-angeles-current-location` | `ca-request`, `ca-la-office` | Los Angeles temporary-service notice; Tips for Submission; official-agency-operational-notice; checked 2026-10-22 |
| `company-certified-versus-plain` | `ca-business-records`, `ca-request` | Certificates, Copies, Status Reports and Processing Times; official-agency-records guidance plus Notary Geek signature-route inference; checked 2026-10-22 |
| `company-other-entity-types` | `ca-business-records` | Note below Plain Copies; official-agency-records guidance; checked 2026-10-22 |
| `company-private-boundary` | `ca-business-records`, `ca-request` | Frequently Requested Information / Bylaws or Operating Agreements; official-agency facts plus Notary Geek output-lane inference; checked 2026-10-22 |
| `company-paper-procurement` | `ca-business-order` | Form p1 items 1–4; p2 items 1/3/4; p3 Fees and Where to Submit; official-agency-guidance; checked 2026-10-22 |
| `company-online-presentation-unresolved` | `ca-business-records`, `ca-bizfile-discovery`, `ca-request` | Business Records; Certified Copies (Online); apostille mail item 1; Notary Geek bounded evidence-gap finding; checked 2026-10-22 |
| `company-supported-paper-alternative` | `ca-business-order`, `ca-business-records`, `ca-request` | Paper order form; other-entity stamp note; apostille mail item 1; Notary Geek route synthesis from official procedures; checked 2026-10-22 |
| `education-issuer-package` | `ca-education-ucdavis`, `ca-education-berkeley` | UC Davis Documents Available for Notarization; Berkeley Apostilles; official-issuer-procedure; checked 2026-10-22 |
| `education-private-institution-chain` | `ca-education-caltech`, `ca-faq` | Caltech Procedure; SOS FAQ 1; issuer procedure plus Notary Geek source-conflict resolution; checked 2026-10-22 |
| `education-notary-copy-boundary` | `ca-notary-handbook-2026` | Printed pp17–19,31–32,44–45; official handbook/statutory text plus Notary Geek scope inference; checked 2026-10-22 |
| `education-private-statement-boundary` | `ca-faq`, `ca-education-berkeley`, `ca-notary-handbook-2026` | SOS FAQ 1; Berkeley Apostilles; handbook copy-certification provisions; Notary Geek signature-route inference; checked 2026-10-22 |
| `court-divorce-issuer` | `ca-cdph-divorce`, `ca-cdc-divorce` | CDPH opening paragraphs; CDC Divorce State/County; official issuer statement corroborated by federal directory; access limit; checked 2026-10-22 |
| `court-certification-vs-exemplification` | `ca-orange-copy-faq`, `ca-faq` | Orange FAQ p2; SOS FAQ 1; court procedure plus Notary Geek boundary inference; checked 2026-10-22 |
| `court-authentication-example` | `ca-stanislaus-g003`, `ca-stanislaus-catalog` | G003 three certificate blocks; catalog Civil/Family Law; official court form via search index; bounded example; checked 2026-10-22 |
| `court-paper-order-return` | `ca-orange-records` | Requesting Copies / Copies of Documents; court procedure plus Notary Geek output inference; checked 2026-10-22 |
| `court-processing-conflict` | `ca-orange-copy-faq`, `ca-orange-records` | FAQ p1 first question; Records page online/mail copy order guidance; unresolved official-source conflict; checked 2026-10-22 |
| `company-inbound-signatures-not-output` | `ca-business-faq`, `ca-request` | Business FAQ Electronic Signatures / Digital Signatures; apostille mail item 1; Notary Geek source-scope inference; checked 2026-10-22 |

## What changed and what did not resolve

- **Company:** The March 2026 paper form supplies a procurement alternative that includes Corp/LLC/LP. Online output to paper acceptance remains CA-Q1. This narrows historical case CA-02 without claiming it passed. The manual-stamp layout describes other entity types; inbound electronic-filing rules do not settle outbound certificate presentation.
- **Court:** Superior Court decree versus CDPH record is separated; Orange certification/exemplification and optional Stanislaus attestation chains identify actual court roles. The final SOS-recognized signer and any additional certification remain CA-Q2. Birth/death County Clerk requirements are not transplanted onto judgments.
- **Education:** Public-university and private-institution preparation examples distinguish registrar provenance from the notarial signature. No universal direct-school-signature or digital-to-paper route is established; CA-Q3 preserves those gaps.
- **Conflicts:** Orange’s undated PDF says 1–3 court days, while the current records page says 30–45 days. The CDC directory’s fee differs from current CDPH indexed information; no CDPH fee is adopted. Caltech’s opening definition is broader than SOS FAQ 1; only its issuer procedure is used. The SOS October 22 queue observation replaces September 28, not a promised turnaround.
- **Access:** CDPH direct retrieval returned 502; indexed official text was corroborated by the directly inspected CDC document distinction. Stanislaus G003 returned 403; its indexed text and current official catalog support a bounded example with its old revision shown. Bizfile portal retrieval returned 403; no portal output was inspected. These are research-access limits, not agency rejections.

## Unresolved agency evidence questions

### CA-Q1 — California Secretary of State: Notary Public Section and Business Entities

For a bizfileOnline-certified filing copy or certificate of status for a corporation, LLC or LP, will the Notary Public Section accept the customer-printed, complete unaltered PDF in its mail and counter apostille lanes? Which signature/certification pages, seal, verification identifier and printing conditions are required? If not, which agency-issued paper certification should be ordered on the March 2026 form?

Supported route pending the answer: Use the published paper certified-record order procedure and preserve the agency certification; inspect the returned output before apostille submission.

### CA-Q2 — California SOS Notary Public Section and originating Superior Court

For a paper Superior Court judgment certified by a named deputy clerk with date and court seal, can SOS authenticate that signature directly? If not, what exact court countersignature, replacement or intermediate certification is required? Is judge-signed exemplification an SOS requirement for this signer or solely a recipient requirement? Is any County Clerk step necessary? What specific paper presentation of electronically certified court records is accepted?

Supported route pending the answer: Request the originating court certified paper record and identify its certifier; obtain an accepted court-issued signature chain when needed. Do not notarize a private photocopy as a substitute.

### CA-Q3 — California SOS Notary Public Section and issuing registrar

Which California public-school registrar signatures, if any, are authenticated directly? For issuer-prepared notarized records, what original paper certification and attachments are required, and is any additional official certification needed for the identified signer? How should an electronically issued transcript/diploma be presented, and can the issuer supply a paper counterpart consistent with recipient sealed-envelope requirements?

Supported route pending the answer: Use the issuing institution published registrar-certification/notarization route and original package; obtain recipient requirements separately.

## Validation and review boundary

Validation covers JSON parsing, unique IDs, source/fact/branch/question references, the explicit wrong-turn decisions, local links, guide/JSON agreement, and unchanged other-state records. It does not execute an answer engine, a provider, a court/registrar order, or an agency acceptance test. Source-and-route review must preserve these limits; an unresolved output is a held branch, not a successful transaction.

## Independent review disposition

Separate agents reviewed source/route conclusions and content consistency without authoring this increment. The source reviewer independently reopened SOS business-order and apostille instructions, the Orange court sources, CDC divorce distinction, and university registrar procedures. No remaining material source/route finding was identified. Stanislaus G003 remains an indexed-evidence example, not an inspected PDF or accepted submission.

The consistency review found and corrected one consequential wrong turn: case **CA-20261022-13** had treated a notary-only diploma copy certificate as if a declarant statement already existed. It now selects `education-notary-copy-certificate`, rejects/replaces the unsupported certificate, and offers no submission route. A separate metadata correction preserves the HCCH entry as an unrefreshed September source record. Neither correction resolves the three named agency questions.

Checks parsed **62 public JSON files**, resolved source/fact/branch/question references and relative links, checked all **17 adversarial cases**, and verified that the other state pilots and historical review remain unchanged. These are content/source checks, not a runtime route test, agency decision, recipient acceptance, or independent legal opinion. This review does not authorize merge or establish complete California coverage.
