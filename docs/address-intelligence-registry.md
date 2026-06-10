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

## Code Home

Azure Function code should live in:

```text
NotaryGeek.PublicKnowledge.Worker
```

Suggested project files:

```text
NotaryGeek.PublicKnowledge.Worker/Configuration/SmartyOptions.cs
NotaryGeek.PublicKnowledge.Worker/Configuration/AddressIntelligenceOptions.cs
NotaryGeek.PublicKnowledge.Worker/Functions/AddressIntelligenceFunction.cs
NotaryGeek.PublicKnowledge.Worker/Models/AddressIntelligenceRecord.cs
NotaryGeek.PublicKnowledge.Worker/Models/AddressEvidenceRecord.cs
NotaryGeek.PublicKnowledge.Worker/Models/AddressEnrichmentRun.cs
NotaryGeek.PublicKnowledge.Worker/Services/AddressIntelligenceRegistryService.cs
NotaryGeek.PublicKnowledge.Worker/Services/SmartyUsStreetAddressEnrichmentService.cs
NotaryGeek.PublicKnowledge.Worker/Services/UspsCmraEvidenceService.cs
```

The static publication target should remain LLCInfo.cc / BusinessIdentitySites:

```text
site/data/address-intelligence-registry.json
site/data/registered-agent-service-addresses.json
site/data/cmra-address-evidence.json
```

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

Do not let Smarty, a Secretary of State roster, a provider web page, or an address-cluster rule overwrite another layer.

## Input Sources

Initial inputs:

- Wyoming Commercial Registered Agent roster;
- other state registered-agent/commercial registered-agent rosters;
- Secretary of State business entity registered-agent records where legally and technically available;
- provider pages advertising registered-agent services at an address;
- provider pages advertising mailbox, mail scanning, forwarding, virtual office, or private mailbox services;
- USPS CMRA-related source material;
- manual confirmations and correspondence when public-safe to summarize;
- source-monitor snapshots.

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

## Smarty Role

Smarty should be used for address validation and enrichment only.

Smarty can help with:

- standardizing street/city/state/ZIP;
- delivery point and ZIP precision if licensed and returned;
- identifying malformed addresses;
- grouping obvious address variants;
- preserving geocoding/address metadata if licensed and returned.

Smarty should not be used alone to decide:

- CMRA status;
- non-CMRA status;
- registered-agent service status;
- provider ownership;
- customer legitimacy;
- bank/platform acceptance.

Credentials stay in Azure Function app settings:

```text
Smarty__AuthId
Smarty__AuthToken
```

## API Shape

Private function routes:

```text
POST /api/address-intelligence/enrichment-runs
GET  /api/address-intelligence/enrichment-runs/{runId}
POST /api/address-intelligence/sources/ingest
POST /api/address-intelligence/usps-confirmations
GET  /api/address-intelligence/export
```

Public JSON outputs:

```text
/data/address-intelligence-registry.json
/data/registered-agent-service-addresses.json
/data/cmra-address-evidence.json
```

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

## Monitoring

Monitor:

- official registered-agent rosters;
- known provider pages;
- USPS DMM/Form pages;
- source URLs already cited by registry records;
- stale records older than the review threshold;
- changed Smarty validation results, if rechecked.

Run types:

```text
source_refresh
address_validation
usps_confirmation_review
public_export
staleness_audit
```

Every export should include:

- generated timestamp;
- source snapshot timestamp;
- schema version;
- input source count;
- address record count;
- evidence record count;
- USPS confirmation disclosure;
- known limitations.

## Implementation Order

1. Define the general address/evidence JSON schema.
2. Move Wyoming CRA address export into the general input model.
3. Add Smarty batch validation as an Azure Function service.
4. Add evidence records for registered-agent service sources.
5. Add CMRA evidence records without overclaiming.
6. Add USPS confirmation workflow and disclosure fields.
7. Publish JSON feeds through LLCInfo.cc.
8. Add monitor jobs and stale-evidence reports.
