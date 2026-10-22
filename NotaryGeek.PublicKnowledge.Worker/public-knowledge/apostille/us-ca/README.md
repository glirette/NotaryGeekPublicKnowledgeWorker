# California apostille and authentication: agent route guide

**Reviewed October 22, 2026 · Partial California route pilot.** [Structured record](us-ca-apostille-authentication.json) · [dated source notes and questions](source-notes-2026-10-22.md) · [wrong-turn cases](adversarial-cases-2026-10-22.json) · [coverage map](../coverage.json)

Start with the **Public-Official Signature Route**: identify the issuer, the signature to be authenticated, the actual output, and the recipient's requested document. California SOS authenticates California public-official signatures for foreign use. An apostille concerns signature, official capacity and seal, not the truth of the document's contents. The recipient's acceptance remains a separate question. [SOS request](https://www.sos.ca.gov/notary/request-apostille) · [FAQ 1](https://www.sos.ca.gov/notary/apostille-faqs)

## Company records: certification before presentation

| What is in hand? | Decision and next step |
| --- | --- |
| Free business-search image or plain copy | Obtain an SOS-certified filing copy if an official certified record is required. A printed filing image, company status search, or private custodian statement is not that certification. |
| Online-certified filing copy or certificate of status | Preserve the complete official output. **Paper presentation remains unresolved:** the reviewed public instructions do not say whether the customer's printout is accepted for apostille, or requires agency-issued paper certification. Verification alone does not answer that question. This is not a finding that all such printouts are rejected. See **CA-Q1** below. |
| Agency-issued paper certified filing or status certificate | Inspect the certification, actual official signature and complete pages, then follow SOS apostille instructions for that returned record. The published paper procurement route is a supported alternative to assuming online-to-paper acceptance; it is not proof of acceptance of an unseen record. |
| Private bylaws or operating agreement | These are maintained by the entity, not filed with SOS. If privately notarized, follow the notary's signature. An SOS status certificate authenticates neither these papers nor their substantive contents. |
| Another state's formation record | Follow that issuer's authority. A California registration/status certificate about a foreign-qualified company is a different California record; it does not authenticate the other state's formation certificate. |

The [records page](https://www.sos.ca.gov/business-programs/business-entities/information-requests) distinguishes certified records from plain copies and private company documents. The [bizfile hub](https://www.sos.ca.gov/business-programs/bizfile) separately lists copy verification and apostille services. Neither supplies a public instruction authorizing the precise printout conversion above. These distinctions are encoded in JSON branch `sos-business-certified`, facts `company-online-presentation-unresolved` and `company-supported-paper-alternative`, and cases CA-20261022-01 through -05.

The **March 2026 [Business Entities Records Order Form](https://bpd.cdn.sos.ca.gov/pdf/be-records-requests.pdf)** includes corporations, LLCs and LPs as well as GP/LLP and other listed types. Select the needed certified filing or status certificate, not a plain copy. It lists $5 per certified document or status certificate. Paper orders go to **BE Certification and Records, P.O. Box 944260, Sacramento, CA 94244-2600**, or drop off at **1500 11th Street, 3rd floor, Room 380**. Mail orders return by mail; drop-off allows mail or pickup and adds $10 per entity. The check/money-order address controls correspondence where applicable. Record procurement and apostille submission are separate stages, with separate fees and addresses; the form does not establish a combined service.

For entity types other than Corp/LLC/LP, the records page specifies a manually stamped certification on the final page's reverse, identifying date, Secretary signature, seal, page count and deputy initials. Do not assume that exact layout applies to every Corp/LLC/LP paper output. California's rules for electronic signatures on filings **submitted to** SOS also do not decide treatment of certified outputs **issued by** SOS.

## Court-issued divorce and judgment records

For a **divorce decree or judgment**, obtain the filed record and necessary attachments from the **originating Superior Court**. CDPH's limited 1962–June 1984 Certificate of Record is a different document and does not establish that the divorce became final. [CDPH issuer page](https://www.cdph.ca.gov/Programs/CHSI/Pages/Vital-Records-Obtaining-Certified-Copies-of-Divorce-Records.aspx) (official indexed text; direct retrieval failed) · [CDC directory, Divorce](https://www.cdc.gov/nchs/w2w/california.htm) (document distinction only, not its fee).

| Stage | Supported branch and stopping point |
| --- | --- |
| Ordinary court certification | Orange County describes a certification stamp/court seal signed and dated by a **Deputy Clerk** on the specified copy. Order the court-issued certified record; an online case summary or private photocopy is not it. |
| Exemplification/authentication package | Orange describes a separate document-listing form prepared by a deputy and signed by a **Superior Court Judge**. Stanislaus optional **G003 (Rev 02/04)** illustrates deputy copy attestation/seal → judge authentication of deputy → deputy authentication of judge/seal. Those clerks are Superior Court officials, not a separate County Clerk by assumption. |
| SOS signature check | Identify the actual certifier and the signature SOS will authenticate. When that signature and paper form are confirmed acceptable, submit the complete court-certified package. If recognition or an intermediate step is unresolved, obtain the court/SOS direction in **CA-Q2** before representing it as ready. No universal direct-deputy acceptance, mandatory exemplification, or County Clerk step is established here. |
| Electronic court record | Online ordering is not proof of an accepted self-print route. Request the court-issued paper counterpart or exact SOS presentation instructions; preserve the certification and attachments. |

[Orange copy FAQ, p.2](https://www.occourts.org/system/files?file=fl_online_copywork_faqs.pdf) · [Orange Family Law Records](https://www.occourts.org/divisions/family-law/family-law-records) · [Stanislaus G003](https://www.stanislaus.courts.ca.gov/system/files/certificate_authentication.pdf) · [current forms catalog](https://www.stanislaus.courts.ca.gov/forms-filing/common-forms-packets). G003 and its catalog were checked through official indexed text; direct PDF access returned 403. It is a concrete local example, not proof of a mandatory or sufficient statewide apostille chain. JSON branches `court-divorce-judgment` and `court-exemplification` preserve that limit.

Orange's online/mail/telephone copy orders produce mailed documents; mail requests need identification of the copies, fees and a sufficiently stamped addressed envelope. Confidential/sealed records retain their access restrictions. Its undated FAQ says **1–3 court days**, while the current records page says **30–45 days**. Neither becomes a deadline promise. Court-copy delivery and SOS return are separate stages. A recipient's demand for exemplification, finality evidence or translation is separate from SOS signature authentication. Do not transplant the birth/death county-certification exception onto court records.

## Education: issuer record versus private statement

For an institutional record, start with the issuing registrar's own certification/notarization procedure. The academic issuer establishes the record's provenance; the subsequent California apostille authenticates the **California notary's signature** in the documented notarial routes. A student's statement about an attached transcript is a different document. JSON branches: `education-issuer-notarized`, `education-private-statement`, `education-digital-or-direct`.

| Record and issuer example | Supported preparation and limit |
| --- | --- |
| UC Davis transcript or verification | Obtain a newly generated original for its Registrar notarization service. Previously issued reproductions are not accepted for that service. |
| UC Davis diploma | Supply the copy its Registrar requests for authentication and notarization. This is input to the issuer's procedure, not permission to send a bare photocopy to SOS. UC Davis offers mail or authorized pickup; its page excludes rush/in-person notarization. |
| UC Berkeley records | Its Registrar offers notarization of official transcripts, verifications and diplomas/diploma copies and explicitly identifies the California notary signature as the apostille target. |
| Caltech records | Its published procedure prepares a registrar certifying cover letter, arranges notarization, and mails to SOS; SOS returns to the requester. Use that procedure for issuer preparation, not the page's overly broad opening definition of apostille as true-copy certification. SOS FAQ 1 controls authentication's effect. |
| Notary-only academic copy certificate | Do not treat this as an eligible issuer record or silently assume a declarant affidavit. Replace it through the issuer procedure; any lawful private declaration is a different document. JSON branch `education-notary-copy-certificate`. |
| Student/custodian education affidavit | Label and route it as a private statement. It does not become the institution's certified record. Establish that the recipient actually accepts that substitute. |
| Digital transcript/diploma or unnotarized registrar signature | Direct authentication of every public registrar's signature and electronic-to-paper presentation are **not established** here. Obtain output/signer-specific instructions; a verification code or an issuer's general view that an apostille is unnecessary is not recipient acceptance. |

Sources checked October 22: [UC Davis](https://registrar.ucdavis.edu/records/notary) (page updated September 8, 2025), [UC Berkeley](https://www.registrar.berkeley.edu/academic-records/notary-services/), [Caltech Procedure](https://registrar.caltech.edu/records/apostille). These examples cover their own records, not every public/private institution. None establishes a mandatory county-clerk stage for the cited registrar-notarization routes. Do not infer that an exceptional signer or output will be accepted without checking it. Confirm any recipient-required sealed-envelope condition before opening or forwarding a package.

The [2026 SOS Notary Public Handbook](https://notary.cdn.sos.ca.gov/forms/notary-handbook-current.pdf), printed pp.17–19 and 31–32, does not grant general notary authority to certify diploma copies. Its separate Government Code **27201.1** tangible-copy procedure uses a custodian declaration/jurat for recording; it is not evidence that a printed academic record qualifies for apostille or becomes issuer-certified. Institution certification followed by notarization must remain distinct from a notary personally certifying a copy.

## Other signature branches retained

| Presented document | Route question |
| --- | --- |
| California birth/death certificate | Inspect the official title. The [SOS FAQ 3](https://www.sos.ca.gov/notary/apostille-faqs) lists county clerks/recorders and deputies and the State Registrar. Health-officer/local-registrar signatures may need county-clerk certification or a newly issued county-recorder/CDPH record. Do not apply that exception automatically to court or school records. |
| Original privately notarized document | Follow the California notary signature; neither notarization nor apostille proves underlying facts. |
| Other-state or federal official record | Identify the actual issuing official and competent authority. California residence, a courier address, or a new private California statement does not authenticate the underlying out-of-state/federal official signature. This is a Notary Geek routing inference from SOS's authority boundary. |

## Submission and return after the document route is settled

| Lane | Instructions checked October 22, 2026 |
| --- | --- |
| Mail to Sacramento | Eligible signed/original notarized/certified document, destination-country cover sheet, $20 per apostille by check/money order, and self-addressed return envelope. USPS: **Notary Public Section, P.O. Box 942877, Sacramento, CA 94277–0001**. Other carriers: **1500 11th Street, 2nd Floor, Sacramento, CA 95814**. Include prepaid tracked-return postage if needed; otherwise return is regular USPS. These are apostille addresses, not the BE record-order address. |
| Sacramento counter | **1500 11th Street, 3rd Floor**. Same-day service; $20 per apostille plus $6 per different public official's signature authenticated; destination-country cover sheet. |
| Los Angeles counter | In-person only; no mail. General address **300 South Spring Street, Room 12513**; current elevator-outage notice directs service to first-floor **suite 1215**. Same $20 and signature-based $6 charges; no cash. Recheck location before travel. |

[SOS request instructions](https://www.sos.ca.gov/notary/request-apostille) · [FAQ 4–7](https://www.sos.ca.gov/notary/apostille-faqs) · [LA notice](https://www.sos.ca.gov/business-programs/los-angeles-office). The $6 counter charge is per different official signature, not automatically per document. Third parties may submit. The general mail rule rejects photocopies; it does not, without more evidence, settle an intact online-certified PDF's paper presentation.

The [processing page](https://www.sos.ca.gov/notary/processing-times), updated **October 22, 2026**, reported mail received **October 14** being processed. Counter submissions were typically processed within 30 minutes, with arrival by 4:30 p.m. advised for service. This replaces the retained September 28 snapshot; it is not an end-to-end deadline. Overnight carriage changes transit, not queue priority. Forms, fees, hours and notices require rechecking at use time.

## Destination, unresolved questions and limits

California uses a single Apostille output for foreign destinations ([FAQ 2](https://www.sos.ca.gov/notary/apostille-faqs)). That output name does not resolve treaty applicability or a non-Convention legalization chain. Check current [HCCH applicability](https://www.hcch.net/en/instruments/conventions/status-table/?cid=41) for the actual destination/date; this increment did not refresh the retained September 29 treaty-source check. Obtain separate recipient evidence for document type, age, translation, sealed delivery and acceptance. No transaction-specific recipient evidence is supplied here.

Prepared questions are recorded in full in the [source notes](source-notes-2026-10-22.md) and JSON `unresolvedQuestions`; none was sent:

- **CA-Q1:** Exact online-certified company PDF presentation in SOS mail/counter lanes, required certification/print conditions, and agency paper alternative.
- **CA-Q2:** Exact Superior Court certifier/signature chain, any needed intermediate certification or exemplification, and accepted paper counterpart of an electronic court record.
- **CA-Q3:** Direct school-official authentication, original issuer-notarial package requirements, electronic presentation and sealed delivery.

This increment supplies supported preparation branches and explicit stopping points. It leaves marriage and other issuer categories, unproven electronic outputs, destination-specific legalization and recipient decisions incomplete. It neither closes California coverage nor changes the other state pilots. Source research and separate-agent review are not agency approval, a live submission test, or a legal opinion.

The terminology and routing distinctions come from the [Notary Geek routing model](https://notary.cx/notarial-routing-model.json), [company-document signature route](../../topics/company-document-apostille-route-by-authenticated-signature.json), and [route-first lexicon](../../notary-community/notary-geek-route-first-lexicon-2026-06-07.md).
