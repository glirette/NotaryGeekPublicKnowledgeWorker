# California single-status and no-record source notes — October 31, 2026

This note supports the [guide](README.md), [structured route](us-ca-apostille-authentication.json) and [16 focused cases](adversarial-cases-2026-10-31.json). It extends the [October 22](source-notes-2026-10-22.md) and [October 28](source-notes-2026-10-28.md) evidence without recasting those reviews or the [September 29 ledger](../adversarial-review-2026-09-29.md) as newly performed. California remains a partial pilot.

## Evidence and dates

Checks below occurred **October 31, 2026 UTC**, at day precision. `checkedUtc` midnight values encode the review day, not a measured fetch time. Source revision/update dates are separate from dates of legal effect. No effective date is inferred for an undated page. Earlier route-fact dates and the October 28 processing observation remain unchanged; re-reading a shared source does not requalify every historical claim.

| Source ID and locator | Official source and access | Exact adopted scope |
| --- | --- | --- |
| `ca-single-status-sd`; By Mail, In Person, third-party FAQ | [San Diego LNR](https://www.sdarcc.gov/content/arcc/home/divisions/recorder-clerk/birth-death-marriage-certificate/single-status-affidavit.html), direct text | Published county procurement: application, identifiers, no required notarization, $19 fee, mail and third-party request. No complete search specification or issued-letter specimen. |
| `ca-single-status-sd-form`; pp.1–2 | [VIT720](https://www.sdarcc.gov/content/dam/arcc/recorder-county-clerk/forms/vitals/Marriage%20Certificate%20Application%20In%20Person%20and%20Mail_20251231.pdf), direct PDF text, revision December 31, 2025 | LNR exception to relationship/identity statement; supplied-information search limitation, fee retention, return fields and in-person photo-ID instruction. This is an application, not the issued letter. |
| `ca-marriage-sd-record`; opening, General Info, confidential and license-county FAQ | [San Diego marriage records](https://www.sdarcc.gov/content/arcc/home/divisions/recorder-clerk/birth-death-marriage-certificate/marriage-certificate.html), direct text refreshed | License-county custody, search failure and registration lag; positive confidential-marriage confirmation differs from LNR. |
| `ca-vital-faq`; FAQ 6–7,22 | [CDPH FAQ](https://www.cdph.ca.gov/Programs/CHSI/Pages/Vital-Records-Frequently-Asked-Questions-%28FAQs%29.aspx), official indexed text; direct retrieval 502 | Certified unsuccessful application-based search and retained fee. No broad person-based all-years single-status service established. |
| `ca-vital-availability`; Copies of Vital Records | [CDPH Important Information](https://www.cdph.ca.gov/Programs/CHSI/Pages/Vital-Records-Important-Information.aspx), official indexed text; direct retrieval 502 | An unavailable database record can yield CNPR; directs first-six-month marriage requests to county. A negative result need not mean no event occurred. |
| `ca-marriage-cdph`; availability and confidential records | [CDPH marriage copies](https://www.cdph.ca.gov/Programs/CHSI/Pages/Vital-Records-Obtaining-Certified-Copies-of-Marriage-Records.aspx), official indexed text refreshed; direct access limitation retained | Year gaps and confidential exclusion. Dedicated page still lists 1905–1999, 2001, 2008–2025; retain the earlier FAQ discrepancy. No completed search inferred. |
| `ca-request`, `ca-faq`; opening, mail items1–4; FAQ1,3,5 | [SOS request](https://www.sos.ca.gov/notary/request-apostille), [SOS FAQ](https://www.sos.ca.gov/notary/apostille-faqs), direct text refreshed | California signature authority, original/certified output and separate fee/return stage. Official capacity is not capacity to marry; birth/death exception is not an LNR-specific chain. |
| `ca-jurat-disclaimer`; required disclaimer | [SOS Jurats](https://www.sos.ca.gov/notary/jurats), direct text | Identity verification is distinct from truth, accuracy or validity; no governmental marriage search arises from notarization. |
| `us-marriage-abroad`; Requirements and Affidavit of eligibility | [State Department Marriage](https://travel.state.gov/en/international-travel/living-abroad/marriage.html), direct text; page updated August 10, 2025 | Destination requirements and possible personal statement; federal government cannot attest marital status. This does not negate a county's narrower search-result service. No destination-specific approval adopted. |
| `ca-sd-signature-services`; Public Health Officer and Notary Public sections | [San Diego signature authentication](https://www.sdarcc.gov/content/arcc/home/divisions/recorder-clerk/signature-authentication.html), direct text | Those services do not establish the LNR's certifier, intermediate step or $21 charge. Broad county notary-intermediate language is not adopted as a universal SOS rule. |

## Claim map and decisions

| New fact ID | Supported decision |
| --- | --- |
| `single-status-document-identity` | Distinguish search result, personal assertion, prior termination and recipient requirements. |
| `single-status-sd-procurement` | County LNR order is actionable; no notarized application is required. |
| `single-status-sd-scope` | Preserve county and input limitations; do not guess years, aliases or confidential coverage. |
| `single-status-cdph-limits` | CNPR can reflect missing indexed data; no comprehensive-status inference or invented statewide order route. |
| `single-status-letter-authentication-gap` | Hold exact LNR/CNPR signer and output before selecting SOS submission. |
| `single-status-private-affidavit` | Authenticate the actual notary's signature, with separate recipient acceptance; private statement remains private. |
| `single-status-positive-record` | Positive marriage confirmation is not no-record evidence; a divorce judgment does not establish no later marriage. |
| `single-status-fees-and-return` | County procurement and SOS authentication have separate fees, return packages and evidence limits. |

Each fact carries its source IDs, locator and checked date in JSON. Eight new branches implement these distinctions. `single-status-san-diego-order` has only the county procurement lane. The issued-letter and CNPR branches expose no SOS lane while the exact signer/output is unresolved. The recipient-mismatch branch blocks a private-affidavit substitution when issuer evidence is required. The private-affidavit branch is expressly conditional on a lawful California notarization and the recipient's document requirements. Existing other-jurisdiction and court branches remain the applicable routes for their actual signatures.

## Conflicts and excluded inferences

San Diego describes its letter broadly in single-status terms. Its record page and application provide a narrower evidentiary basis: local custody and a result based on supplied information. This corpus adopts those limits, not worldwide or statewide proof of absence. The form's public/LNR and confidential/confirmation selections do not prove exactly which indexes an LNR search includes. Neither inclusion nor exclusion of confidential records is asserted.

The dedicated LNR webpage supplies a date-of-birth instruction although the general marriage form has marriage/date fields rather than a dedicated LNR search specification. Do not invent an all-years search range or per-year price. The webpage payee is San Diego Recorder/County Clerk; the linked form uses San Diego County Recorder. This wording difference remains visible. Neither source documents LNR-specific overseas tracking or a processing guarantee.

CDPH's certified CNPR label establishes document identity, not its exact apostille-ready signature. Its general paper-vital-record guidance and SOS's original/certified-document rule do not qualify an unseen CNPR. A requested marriage copy, a CNPR arising from search failure and a deliberately scoped no-marriage search must not be conflated. Ordinary search fees are not evidence of the latter service.

## Unresolved evidence questions

**CA-Q5 — San Diego and SOS:** Which indexes, dates, names/aliases and confidential classes does the LNR search cover? What limitations/registration lag does the issued text state? Which official title, signature and seal appear? Is that intact paper output accepted directly by SOS, or does it need identified intermediate certification? Is any electronic-to-paper presentation accepted?

Supported route: obtain the published county search result when that is what the recipient requests, retain the actual issued package, and resolve the signature/output gate before SOS submission. A new private notarization is not a substitute for issuer evidence.

**CA-Q6 — CDPH and SOS:** Is there a person-based no-marriage search distinct from an unsuccessful certified-copy request? If so, what application, fee, search coverage, attestation, certifier and output apply? What actual CNPR signature and intermediate certification does SOS require, including any electronic-to-paper conditions?

Supported route: interpret an existing CNPR within its stated search limits. Use the researched county procedure if the recipient wants that evidence; a private affidavit remains a separate, recipient-dependent route. Prior CA-Q1–CA-Q4 remain unresolved.

## Review limits

Public-source research does not inspect an issued LNR/CNPR, recognize an actual certifier, test an order or submission, establish recipient acceptance, or determine anyone's marital status. No complete statutory analysis or destination-specific law review is represented. Official indexed CDPH evidence carries the direct-access limitation above. The 16 new wrong-turn cases supplement 29 earlier California cases; these are content and source checks, not an executed production route engine. Other state/federal pilots retain their dates and content.

## Validation and independent review

Independent source/route and consistency reviewers examined the candidate without authoring it. The source review rechecked controlling county, SOS and State Department sources, with the disclosed CDPH indexed-text limitation. It resolved a material branch mismatch: a private affidavit offered where issuer evidence is required now selects an explicit hold with no SOS lane. A further guard limits the prior-divorce cross-reference to California-issued court judgments. No material source finding remains.

Consistency validation passed 2,917 assertions: 64 public JSON files parsed with duplicate-key checks; all 45 California cases and source/fact/branch/question/lane references resolved; 67 relative Markdown links and anchors checked; all 16 non-California state/federal pilot files remained byte-identical. Previous California facts, branches, CA-Q1–CA-Q4, submission lanes, issuer examples and case ledgers were preserved. Shared-index changes are limited to California entries and version increments. The publication-boundary checker reported zero findings in eight changed files; manual public-only review found no commercial strategy or correspondence in the public patch. These checks are content validation, not a production routing or agency acceptance test. A full application build was not run because the increment changes public text and data only.
