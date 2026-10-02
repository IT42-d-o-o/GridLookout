# GridLookout — Commercial Licensing

GridLookout is free for personal, hobby, research, and other noncommercial use under
[PolyForm Noncommercial 1.0.0](../LICENSE). **Any business, government, or otherwise commercial
use requires a commercial license** purchased from the author (IT42 d.o.o., Croatia).

A draft of the agreement you'll sign is available for review before you buy:
[GridLookout Commercial License Agreement (template)](gridlookout-commercial-agreement-template.md).

## License metric: the wall-controller PC

One license covers **one Windows machine running GridLookout** — with **unlimited attached
monitors and unlimited cameras** on that machine. Monitors and camera counts never change the
price; only the number of PCs running the software does. A licensed PC can be replaced
(hardware refresh, reinstall) at no cost — the license moves with you, not with the box.

## Pricing

Every license is available two ways: **perpetual** (one-time, yours forever, first 12 months of
updates included) or **yearly subscription** (all updates and support included while active).
Licenses come as a **Single Controller** (one wall-controller PC), a **5-Pack** (five controllers, one
end customer, any sites, plus one assisted deployment session) and a **10-Pack** (ten controllers, a
named technical contact, a rollout review and next-business-day operational response). Larger
fleets are priced per controller under one master agreement.

**Current prices are published on the buy page: https://gridlookout.it42.hr/buy** - the repository does not
restate them, so the page is always the figure that applies. Maintenance for perpetual licenses
(optional, after the included first year) is a yearly percentage of the license price and is quoted
on the same page; it brings continued updates, security and XProtect-compatibility releases and
email support. A subscription always includes all of that while active, plus license rehosting when
a controller PC is replaced.

**Which one do you need?**

- **One to four walls -> Single Controller licenses** (perpetual if the wall is a long-lived
  fixture, yearly if you prefer opex or expect the estate to change).
- **Five or more walls -> 5-Pack / 10-Pack**, any mix of sites under one end customer.
- **Larger fleets** -> contact us for the master agreement.

## Terms summary

- **Perpetual**: a purchased license never expires. The first 12 months of updates are
  included; support during that period is as stated in your quote. After the first 12 months,
  updates and support continue only with active maintenance — a lapsed customer keeps using the
  version they have, forever.
- **Yearly subscription**: licensed while the subscription is active, including all updates,
  security and compatibility releases, email support, and rehosting. A lapsed subscription means
  commercial use must stop (unlike a perpetual license) — there is still no activation server or
  phone-home involved (see the next bullet); the term is enforced by the agreement itself, so
  air-gapped sites remain fully supported on subscription too.
- **No telemetry, no activation server**: licensing is enforced by the commercial agreement
  itself — the software never phones home to check or report license status. Air-gapped sites
  are fully supported. (The integration does identify itself to the customer's own Milestone
  XProtect system it logs into, and that system's own telemetry channel — a setting entirely
  under the customer's control — may in turn report installed integrations to Milestone;
  see the Security & Network Behavior guide's "Network connections" for the full disclosure. This is separate
  from, and does not compromise, the no-activation-server licensing model above.)
- **A separately licensed Milestone XProtect system is required.** GridLookout does not
  include, replace, or modify any Milestone licensing. Redistribution of the bundled Milestone
  MIP SDK runtime is authorized by the MIP SDK license agreement; see the third-party notices
  file — `NOTICES.md` at the application's install root once installed, `docs/gridlookout-NOTICES.md`
  in this repo — for the authorization statement and all third-party licenses.
- Provided **as-is, without warranty**; see the license agreement issued with your invoice
  for the complete terms (the template above shows the terms that agreement is drawn from).
- **30-day commercial evaluation**: before buying, a prospective commercial customer may run
  GridLookout in a commercial environment for up to 30 days under the
  [PolyForm Free Trial License 1.0.0](../LICENSE-TRIAL) — obtained directly from the licensor
  (contact address below), not redistributed by an integrator. The trial license permits evaluation use only and does not
  permit further distribution of the software. At the end of the evaluation window you must
  either purchase a commercial license or stop commercial use; continuing commercial use beyond
  the trial period without either is not licensed.

## How to buy

**Single Controller and 5-Pack licenses (1.0.2 build of 2026-09-27 and later)**: buy directly at
**https://gridlookout.it42.hr/buy** - checkout, invoice, and licence delivery are
instant and self-service; see "Licence delivery and installation" below for what arrives and how
to install it.

**10-Pack, larger fleets, or an integrator/reseller deal**: email **info@it42.hr** with:

1. Number of wall-controller PCs and sites
2. Your company name and billing details
3. (Optional) your Milestone integrator, if one is deploying it for you - integrator
   partners receive reseller terms

You receive a quote, then an invoice and the signed license agreement, with the licence file
delivered the same way described below.

## Licence delivery and installation (1.0.2 build of 2026-09-27 and later)

A purchase - self-service or via quote - delivers one signed licence file, `gridlookout.lic`, by
email, instantly after payment clears. No account, no download portal login, no activation step.

To put it to work on a wall-controller PC, either:

- Run `GridLookout.exe --license-install <path-to-gridlookout.lic>` (recommended - verifies the
  file and installs it in one step). An already-running wall picks it up automatically within
  `ConfigRefreshSeconds`, no restart.
- Copy the file next to `camerawall.json` by hand, or point `LicensePath` in `camerawall.json` at
  it.

Full mechanics - the trial, `--license`/`--license-fingerprint`, what an unlicensed wall looks
like, and the air-gap/no-phone-home guarantee - are in
[GridLookout Licensing](gridlookout-licensing.md). Before buying, every prospective customer
already gets a full-featured **30-day trial automatically** on first run - no request needed; see
that document's "The 30-day trial" and the "30-day commercial evaluation" terms below for how the
two relate (the automatic trial is the mechanism; the terms below govern commercial use during
it).
