# Address Intelligence Registry

Created: 2026-06-10

## Purpose

Build a public-safe address intelligence registry that can ingest many business-identity address sources, not only registered-agent rosters.

The registry should answer source-backed questions such as:

- Which known addresses provide or advertise registered-agent services?
- Which addresses are confirmed CMRA, confirmed non-CMRA, likely CMRA, disputed, or unknown?
- How do we know?
- Which source last confirmed the answer?
- How stale is the evidence?
- What does USPS officially confirm, and what does USPS not confirm?

This is an address-first system. Provider names, brand names, registered-agent names, mailbox brands, virtual office brands, and Secretary of State roster entries attach to an address record as evidence. They do not replace the address record.

## Public Surface Boundary

This public note describes the source-quality model for an address intelligence registry. It should not describe private code layout, hosting, deployment targets, provider account configuration, worker names, job schedules, publication pipelines, or internal storage paths.

Public outputs may identify LLCInfo.cc as the business-identity companion source layer when the relevant public feeds or pages already exist. They should not instruct readers how Notary Geek or GoodWare builds, publishes, monitors, or deploys those feeds.

## Source Boundary

Keep these layers separate:

- raw source address text;
- parser-normalized address key;
- postal/address validation result;
- registered-agent service evidence;
- CMRA evidence;
- USPS confirmation evidence;
- human review notes;
- public disclosure.

Do not let an address-validation provider, a Secretary of State roster, a provider web page, or an address-cluster rule overwrite another layer.

## Input Sources

Initial inputs:

- Wyoming Commercial Registered Agent roster;
- other state registered-agent/commercial registered-agent rosters;
- Secretary of State business entity registered-agent records where legally and technically available;
- provider pages advertising registered-agent services at an address;
- provider pages advertising mailbox, mail scanning, forwarding, virtual office, or private mailbox services;
- USPS CMRA-related source material;
- manual confirmations and correspondence when public-safe to summarize;
- public source snapshots.

## Evidence Statuses

Registered-agent service status:

```text
confirmed_registered_agent_service
advertised_registered_agent_service
roster_listed_registered_agent
entity_record_registered_agent
historical_registered_agent_service
not_registered_agent_service
unknown_registered_agent_service
```

CMRA status:

```text
usps_confirmed_cmra
usps_confirmed_non_cmra
provider_confirmed_cmra
provider_confirmed_non_cmra
likely_cmra
likely_non_cmra
disputed_cmra_status
unknown_cmra_status
```

Confidence should be separate from status:

```text
official
high
medium
low
lead_only
```

## USPS Evidence

Official baseline sources:

```text
https://pe.usps.com/text/dmm300/508.htm
https://about.usps.com/forms/ps1583a.pdf
https://about.usps.com/forms/ps1583.pdf
```

USPS-backed CMRA evidence should capture:

- USPS source type;
- source URL or contact method;
- local post office or USPS unit if applicable;
- date checked;
- person/office/title when available and public-safe;
- exact question asked;
- answer category;
- whether the response was official, informal, written, verbal, or automated;
- limitations stated by USPS;
- whether the evidence can be published directly or only summarized.

Public disclosure should avoid overstating USPS certainty. If there is no official public USPS CMRA API or complete public list used by the registry, say that plainly.

Suggested disclosure text:

> CMRA status in this registry is evidence-based, not a live USPS master list. Records marked USPS-confirmed include the date and method of confirmation. Records marked likely, disputed, or unknown require further source review. Address-validation providers can help standardize addresses, but they do not prove CMRA status by themselves.

## Address-Validation Provider Role

Address-validation providers should be used for address validation and enrichment only.

Address-validation evidence can help with:

- standardizing street/city/state/ZIP;
- delivery point and ZIP precision if licensed and returned;
- identifying malformed addresses;
- grouping obvious address variants;
- preserving geocoding/address metadata if licensed and returned.

Address-validation evidence should not be used alone to decide:

- CMRA status;
- non-CMRA status;
- registered-agent service status;
- provider ownership;
- customer legitimacy;
- bank/platform acceptance.

## Public JSON Record Shape

```json
{
  "addressId": "addr_...",
  "canonicalAddress": {
    "singleLine": "",
    "street": "",
    "city": "",
    "state": "",
    "postalCode": "",
    "country": "US"
  },
  "sourceAddressVariants": [],
  "registeredAgentService": {
    "status": "unknown_registered_agent_service",
    "confidence": "lead_only",
    "evidenceIds": []
  },
  "cmra": {
    "status": "unknown_cmra_status",
    "confidence": "lead_only",
    "evidenceIds": [],
    "uspsConfirmation": {
      "status": "not_checked",
      "checkedAt": null,
      "method": null,
      "limitations": []
    }
  },
  "providers": [],
  "sourceRecords": [],
  "lastReviewedAt": null,
  "lastSourceSeenAt": null,
  "warnings": []
}
```

## Public Review Signals

Public review should watch for:

- official registered-agent rosters;
- known provider pages;
- USPS DMM/Form pages;
- source URLs already cited by registry records;
- stale records older than the review threshold;
- changed address-validation evidence, if rechecked.

Public observation categories:

```text
source observation
address validation evidence
USPS confirmation evidence
public disclosure
staleness review
```

Every public record or feed should disclose:

- generated timestamp;
- source snapshot timestamp;
- schema version;
- input source count;
- address record count;
- evidence record count;
- USPS confirmation disclosure;
- known limitations.

## Public Development Sequence

1. Define the public address/evidence schema.
2. Document the source roles without collapsing registered-agent, CMRA, mailbox, virtual-office, and business-address roles.
3. Add registered-agent service evidence without treating a provider page, state roster, or cluster rule as the whole answer.
4. Add CMRA evidence without overclaiming when USPS or provider confirmation is incomplete.
5. Add USPS confirmation disclosure fields.
6. Add public staleness and limitation language.
7. Expose only public-safe records and source summaries through the public business-identity source layer when ready.
