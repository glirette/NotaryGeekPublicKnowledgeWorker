# California marriage-record source notes — October 28, 2026

This note supports the [California guide](README.md), [route record](us-ca-apostille-authentication.json) and [12 marriage wrong-turn cases](adversarial-cases-2026-10-28.json). It extends the [October 22 evidence](source-notes-2026-10-22.md); neither that review nor the [September 29 ledger](../adversarial-review-2026-09-29.md) is presented as newly performed. California remains a partial pilot.

## Evidence and source scope

Checked dates below mean research observations on **October 28, 2026**, with day precision. They are not effective dates or midnight fetch times. No date of legal effect is inferred for an undated page. The current legislative pages for Family Code 509/511 and HSC 103526 returned 403; the narrow access conclusions rely on directly retrieved county guidance citing those sections, not newly inspected complete statutory text.

| Source ID | Official source / locator | Access and limit |
| --- | --- | --- |
| `ca-marriage-directory` | [CDPH directory](https://www.cdph.ca.gov/Programs/CHSI/pages/county-registrars-and-recorders.aspx), Marriage Records | Official indexed text; direct retrieval 502. Public Recorder/confidential Clerk and license county, corroborated by county procedures. |
| `ca-marriage-cdph` | [CDPH marriage copies](https://www.cdph.ca.gov/Programs/CHSI/Pages/Vital-Records-Obtaining-Certified-Copies-of-Marriage-Records.aspx), opening and Mail In Request | Official indexed text; direct retrieval 502. Availability observation and confidential-record exclusion, not a successful order. |
| `ca-vital-faq` | [CDPH FAQ](https://www.cdph.ca.gov/Programs/CHSI/Pages/Vital-Records-Frequently-Asked-Questions-%28FAQs%29.aspx), 5, 8, 11, 17, 23–25 | Official indexed text; direct retrieval 502. Application notarization, paper output and apostille-purpose instruction; year discrepancy retained. |
| `ca-marriage-sacramento` | [Sacramento marriage certificates](https://ccr.saccounty.gov/us/en/marriage-certificates.html), Public/Confidential Marriage Records and By Mail | Direct page. Eligibility and copy types; do not use its erroneous birth-data field list under Online as marriage instructions. |
| `ca-marriage-smc-apostille` | [San Mateo apostille information](https://smcacre.gov/county-clerk-recorder/apostille-information), sentence expressly including Clerk-Recorder marriage certificates | Direct page. Adopt only its issuer-specific direct route; do not generalize its other notary or provider statements. |
| `ca-marriage-smc-order` | [San Mateo marriage certificates](https://smcacre.gov/county-clerk-recorder/marriage-certificates), mail, license county, return and overseas application paragraphs | Direct page. County procedure only; quoted delivery estimates are not adopted as deadlines. |
| `ca-marriage-smc-form` | [Currently linked application](https://smcacre.gov/media/1134/download?attachment=), pp.1–3 | Direct PDF text, not a submitted form or specimen certificate. No revision date stated. The current linked form lists $19. |
| `ca-marriage-smc-fees` | [San Mateo fees](https://smcacre.gov/county-clerk-recorder/vital-records-fees), Marriage Certificate/general public | Direct page. $19 record-copy fee; not the SOS fee. |
| `ca-marriage-sd-record` | [San Diego marriage record](https://www.sdarcc.gov/content/arcc/home/divisions/recorder-clerk/birth-death-marriage-certificate/marriage-certificate.html), introduction, By Mail and registration FAQ | Direct page. Registered record, application notary and county-custody distinctions; do not import its local processing estimates elsewhere. |
| `ca-marriage-sd-keepsake` | [San Diego keepsake](https://www.sdarcc.gov/content/arcc/home/divisions/recorder-clerk/marriage-license-civil-ceremony/marriage-keepsake.html), souvenir notice | Direct page. County branding, gold seal and notarized procurement do not establish certified-record identity. |
| `ca-request`, `ca-faq` | [SOS request](https://www.sos.ca.gov/notary/request-apostille) and [FAQ](https://www.sos.ca.gov/notary/apostille-faqs), original/certified document, return, fees; FAQ 1, 3, 5–7 | Direct pages refreshed. State instructions control the SOS stage; birth/death-specific text is not a universal marriage rule. |
| `ca-processing`, `ca-la-office` | [SOS processing](https://www.sos.ca.gov/notary/processing-times) and [LA office](https://www.sos.ca.gov/business-programs/los-angeles-office) | Direct pages refreshed. Processing page displayed October 27 update and mail received October 16. LA temporary suite 1215 remains published. Volatile observations, not service guarantees. |

Earlier business, court, education and HCCH sources retain their prior dates. Refreshing the shared SOS pages does not freshly qualify every claim that cites them. Each `routeFacts` item carries its own checked date.

## Claim map

| New fact ID | Source IDs | Scope of conclusion |
| --- | --- | --- |
| `marriage-issuer-and-registration` | `ca-marriage-directory`, `ca-marriage-sd-record` | Issuer and registered-record identity; ceremony county is insufficient. |
| `marriage-cdph-availability` | `ca-marriage-cdph`, `ca-vital-faq` | Dated differing year ranges; county alternative, no negative marriage-status inference. |
| `marriage-copy-type-and-access` | `ca-marriage-sacramento` | Certified authorized/informational public copies versus restricted confidential copies. |
| `marriage-requester-not-submitter` | `ca-faq`, `ca-marriage-sacramento` | Inference: permission to present a document does not confer access to it. |
| `marriage-application-not-certification` | `ca-marriage-sd-record`, `ca-request` | Inference: application notarization and the issued record have different signatures. |
| `marriage-smc-direct-route` | `ca-marriage-smc-apostille`, `ca-faq`, `ca-request` | San Mateo paper-record route only; normal SOS request items remain required. |
| `marriage-smc-order-return` | `ca-marriage-smc-order`, `ca-marriage-smc-form`, `ca-marriage-smc-fees`, `ca-request` | Record procurement and SOS handling are separate paid stages with separate returns. |
| `marriage-overseas-application-scope` | `ca-vital-faq`, `ca-marriage-smc-order` | Different custodians publish different application-notarization instructions. |
| `marriage-nonrecord-output` | `ca-marriage-sd-keepsake`, `ca-marriage-sd-record`, `ca-request` | Souvenir/application/private-copy identity is not an issuer-certified marriage record. |
| `marriage-cdph-apostille-preparation` | `ca-vital-faq`, `ca-request` | Apostille-purpose request leads to further instructions, not a CDPH apostille. |

## Source conflicts and excluded shortcuts

- **CDPH year ranges:** the dedicated page lists 1905–1999, 2001 and 2008–2025; FAQ 8 lists 1905–1999 and 2008–2023. Both observations came from the official search index, whose refresh is outside this review's control. Confirm availability or use the license-issuing county. No finding that a later event or record does not exist follows from the published range.
- **Overseas applications:** CDPH FAQ 24 includes a foreign notary with apostille; San Mateo's own page excludes foreign-notary acknowledgments and identifies other officers. These are different custodians' procedures. The record-order office must resolve the applicable application path; this note supplies no universal harmonization.
- **San Mateo page scope:** its marriage-certificate direct-route sentence is the adopted evidence. Its broader notary-intermediate-step and provider language is not adopted as a statewide requirement or authority delegation. California SOS remains the competent authority. “Without additional documentation” does not waive SOS cover sheet, payment or return materials.
- **Informational copies:** certification, identity use, SOS authentication and recipient acceptance are distinct. No reviewed source conclusively decides every informational marriage certificate's exact presentation. Do not convert that gap into either universal rejection or approval.
- **Online ordering:** a vendor order or electronic application does not demonstrate issuance of an apostille-eligible electronic certificate. CDPH FAQ 5 expressly excludes digital/scanned/faxed vital-record images. A downloaded form is an application, not the resulting record.

## Remaining evidence question: CA-Q4

For the actual marriage-record certifier and complete proposed output, does SOS authenticate that signature directly, or require a specific issuer countersignature or replacement? Does it accept that certified informational copy or exact electronic-to-paper presentation? The receiving organization must separately establish whether that copy type, translation and delivery form satisfy its requirements.

Supported route while these conditions are unresolved: obtain an issuer-issued authorized paper record through an eligible requester when that is the required document. San Mateo publishes a direct route for its Clerk-Recorder marriage certificates. That example does not approve an unseen certificate from any office, and no confidential-record access rule is bypassed.

## Review limits

This is public-source research and content validation. It does not inspect a real certificate, recognize a named certifier, place an order, test an agency submission or establish recipient acceptance. It does not independently verify complete current statutory text. The 12 new cases supplement the earlier 17 California cases; they are source-linked decision checks, not executed production routing tests. Other state records, the original six-state review history and unrelated runtime/source-client work remain outside this increment.

## Validation and independent review

Separate source/route and consistency reviewers examined this increment without authoring it. The source reviewer reopened the consequential SOS, county and CDPH sources, including the current San Mateo application, and found no remaining material route issue. Direct CDPH and legislative access limits remained visible. The consistency review passed 832 assertions: 63 public JSON files parsed; all 29 California cases, source/branch/question references and 55 relative Markdown links resolved; the 16 other state/federal pilot guide-and-record files remained unchanged. The publication-boundary checker reported no findings in the nine changed files. These checks do not qualify unrelated branches or constitute an agency acceptance test.

Final review corrected processing-source metadata to `pageUpdated: 2026-10-27` with no inferred effective date, and made the San Mateo direct branch explicitly require an authorized paper copy. Prior company, court and education branches remain intact. A full application build was not run because this increment changes public text and data only.
