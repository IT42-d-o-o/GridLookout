# Security Policy

## Scope

This policy covers **GridLookout for Windows** (`src/GridLookout`), IT42's MIP SDK video-wall
client for Milestone XProtect. The **IT42 Estate Emulator** (`MilestoneRecorderEmulator` /
`MilestoneSwipeEmulator`) and **MilestoneDashboard** (`src/MilestoneDashboard.Api`), both built
from this same repository, share this policy - the reporting channel, triage process, and
remediation targets below apply to all three products. Product-specific network and credential
behavior is documented separately: see `docs/camerawall-security.md` for GridLookout.

This policy describes practice **aligned with** NIST SP 800-218 (Secure Software Development
Framework) and ISO/IEC 27001:2022 Annex A development controls - see
`docs/secure-development-policy.md` for the control-by-control mapping. IT42 d.o.o. is **not**
ISO 27001 certified and has not commissioned a third-party security audit; nothing in this
document or the linked policy should be read as a certification or audit claim.

## Reporting a vulnerability

Email **security@it42.hr** with details. This is a plain email address - PGP/encrypted mail is
not currently offered, so avoid pasting exploit code or credentials directly in the message body
if the finding is sensitive; a proof-of-concept description is normally sufficient for triage.

Please do **not** file a public GitHub issue for a suspected security vulnerability. Public issue
trackers are for functional bugs and feature requests only.

**Please include:**
- Affected product and version (e.g. `GridLookout 1.0.2`)
- A description of the vulnerability and its potential impact
- Steps to reproduce, or a proof-of-concept if available
- Any relevant logs, configuration, or environment details (redact hostnames/IPs/credentials that
  are not needed to reproduce the issue)

## What to expect

- **Acknowledgement**: within 5 business days of the report.
- **Triage**: severity is assessed using CVSS v3.1.
- **Remediation targets** (from acknowledgement, by CVSS v3.1 severity):

| Severity | Target |
|---|---|
| Critical | 30 days |
| High | 60 days |
| Medium | 90 days |
| Low | Next scheduled release |

These are targets, not guarantees - a fix that requires a coordinated change across the affected
product and its dependencies (e.g. the Milestone MIP SDK) may take longer, and the reporter will
be kept informed if a target slips.

## Coordinated disclosure

We ask reporters to hold public disclosure until **90 days after the report, or until a fix ships,
whichever comes first**. We will keep the reporter updated on progress throughout.

## Advisories

Security advisories for fixed vulnerabilities are published in the affected product's release
notes and sent directly to known customers (those with an active commercial license or support
relationship). There is no separate public advisory feed at this time.

## Safe harbor

Good-faith security research conducted in accordance with this policy - testing against your own
lab/trial installation, no access to other customers' data, no denial-of-service testing against
production systems, and prompt reporting of anything found - is authorized. We will not pursue
legal action for such research.

## Bug bounty

There is no paid bug bounty program at this time. We are grateful for responsible disclosure and
will credit reporters (with their permission) in the relevant release notes.

## Supported versions

Only the **latest released version** receives security fixes. Current release: **GridLookout
1.0.2**. Customers on an older version should upgrade before reporting an issue, to confirm it is
still present.
