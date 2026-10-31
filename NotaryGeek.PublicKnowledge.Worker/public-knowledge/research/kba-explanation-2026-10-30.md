# When the identity quiz fails, identify the route that can actually work

Notary Geek / GoodWare LLC / Greg Lirette · October 30, 2026

Public research draft for Greg’s customer-guidance review. This supplements the existing [KBA data-source explanation](https://notarygeek.net/ron-kba-public-record-proprietary-record.html) and [Proof/foreign-signer guide](https://notarygeek.net/proof-kba-foreign-signer-escrow.html). The [claim/source matrix](kba-claim-source-matrix-2026-10-30.json) retains evidence limits; the [routing proposal](kba-routing-proposal-2026-10-30.json) has matching synthetic regression examples.

KBA tests knowledge associated with an identity. The criticism concerns what that establishes: knowing database facts differs from demonstrating that the person presenting an identity document owns it. An honest person can lack the expected records. Another person can possess the facts. Neither possibility supplies a failure rate, and a failed quiz does not establish fraud.

[GAO’s federal review](https://www.gao.gov/products/gao-19-288) explains how breached information could enable KBV impersonation, while recognizing barriers to alternatives. This supports a threat model, not a finding that Proof was breached or every quiz can be beaten. Proprietary records may be harder to obtain than public records. Guessability, exposure and successful impersonation are different claims. [K02–K03, K07]

Our public/proprietary-record terminology needs a product qualification: [Proof describes credit-history inputs](https://support.proof.com/hc/en-us/articles/10468298021271-Knowledge-Based-Authentication). [LexisNexis describes a commercial question engine](https://risk.lexisnexis.com/products/instantid-q-and-a) using linked data and non-credit content. That does not identify Proof’s supplier. Credit-related inputs are not automatically a credit score or ordinary consumer report; absence of a visible credit-permission screen cannot settle data sources or legal classification. [K03–K05, K22]

## Keep four questions separate

| Question | Evidence needed |
|---|---|
| What does law allow? | Notary jurisdiction, transaction date, statute and adopted standards. |
| What assurance does the method provide? | Named standard/revision, actual controls and performance evidence. |
| What can this invitation do? | Product, settings, eligibility and completed method results. |
| Will the document be accepted? | Written recipient requirements and output format. |

[NIST SP 800-63A-4 §2.4.2](https://pages.nist.gov/800-63-4/sp800-63a.html#identity-verification) says KBV/KBA **“SHALL NOT be used for identity verification.”** Section 3.2.1 permits KBV for fraud management. A supplemental quiz therefore does not replace independent verification. A product’s “supplemental” label alone establishes no conformity. This technical rule is not a blanket prohibition in state notary law. [K01]

Virginia illustrates the distinction. [The 2021 enacted text](https://legacylis.virginia.gov/cgi-bin/legp604.exe?212+ful+CHAP0078+pdf=) did not expressly list KBA. [Chapter 832](https://legacylis.virginia.gov/cgi-bin/legp604.exe?241+ful+CHAP0832+pdf=) added it effective July 1, 2024, while its retrospective clause names the separate validation subsection. Under [current §47.1-2](https://law.lis.virginia.gov/vacode/title47.1/chapter1/section47.1-2/), KBA is an option within a two-method route. Permission establishes neither comparative strength nor what a platform performed. Selfie alone does not satisfy the signer-certificate/PIV wording. [K12–K16]

When KBA is absent, ask **which authorized route was completed and what supports it**. Personal knowledge must be real; a witness needs the required qualifications and oath. A two-method route needs distinct authorized methods. For an adopted alternative, identify its dated authority. For certificate/PIV, establish the signer’s qualifying credential and access event. An IAL2 label supplies no missing statutory mapping. [K15–K16]

The structured proposal enumerates the four current Virginia listed methods other than KBA and the evidence each assertion needs. Recommended method-result evidence is separate from the statutory record, recording and identification notation in [§47.1-14](https://law.lis.virginia.gov/vacode/47.1-14/). Sensitive evidence belongs in protected records. [K17]

## Diagnose the barrier before choosing another path

No questions, wrong answers, retry lockout, unreadable ID and account conflict require different responses. Proof documents these barriers without supplying measured prevalence. Its [general failure guide](https://support.proof.com/hc/en-us/articles/42960658744983-Identity-Verification-Failed-Here-s-What-to-Do) conflicts with the dedicated quiz guide over whether one wrong answer fails. Instructions are evidence to reconcile, not transaction telemetry. [K05–K08]

An eligible [no-SSN route](https://support.proof.com/hc/en-us/articles/36549115702295-Verify-Your-Identity-Without-a-Social-Security-Number) exists, but retains a U.S.-address condition. It does not establish availability for someone who never had that history. The [overview](https://support.proof.com/hc/en-us/articles/31945915880343-Identity-Verification-Overview) makes settings and notary state relevant. Ask the sender which alternative is enabled and acceptable. Do not invent an address, obtain an ITIN merely to feed a quiz, or promise support can erase lockout. [K09–K10]

Foreign-passport support is also separate from completing the configured verification. [Verify’s agent product](https://support.proof.com/hc/en-us/articles/28519849379991-Verify-Transactions-Overview) is not automatically a substitute notarial act. Recipient acceptance must be checked for the document and output. [K11, K19–K20]

The strongest defense is that difficult proprietary questions, time limits and other checks can contribute to layered fraud controls. That is a fair defense of a limited role. It does not establish universal availability or override A-4 verification requirements. Removing KBA likewise does not prove every biometric replacement is safe or accessible. No comparative failure rate was measured here. [K01–K07, K23]

## Where Notary Geek can help

**Publicly described help:** our existing [no-SSN](https://notarygeek.net/no-ssn-required-online-notary.html) and [outside-U.S. passport](https://notarygeek.net/supported-passports-by-notary-geek-for-people-not-inside-the-united-states.html) pages describe eligible intake and route review. Customers blocked by a U.S.-data quiz can use those maintained paths with the document, signer location, available ID, destination and recipient instructions. Review determines whether the existing offering fits. [K21]

**Service possibilities requiring separate confirmation:** document/recipient assessment, help framing an alternative-method request to the sender, and coordination of an appropriate notary/apostille/output route could address these barriers. This creates no new package or price and promises no pass, deadline, country-wide availability or escrow acceptance.

A method concern is not automatic voidness: [Virginia §47.1-20.1(B)](https://law.lis.virginia.gov/vacode/47.1-20.1/) is material counter-law on validity and remedies. Preserve that distinction while seeking compliance evidence. No individual act is adjudicated. [K18]
