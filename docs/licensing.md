# GridLookout Licensing (1.0.2 build of 2026-09-27 and later)

This document covers the **mechanics** of licensing GridLookout from 1.0.2 onward: the trial, the
licence file, the `--license*` command-line switches, what an unlicensed wall looks like, and the
air-gap/no-phone-home guarantee. For pricing, terms, and how to buy, see
[Commercial Licensing](camerawall-commercial-license.md).

## The 30-day trial

A fresh install with no licence file runs a full-featured 30-day trial automatically - nothing to
request, nothing to enter. The trial starts the first time GridLookout runs and is tracked by a
small marker file, `gridlookout.trial.json`, in the same writable state directory as
`camerawall.json` and `health.json` (normally next to the executable; `%ProgramData%\GridLookout`
on a locked-down kiosk account - see `camerawall-security.md`'s "Writable-state fallback").

During the trial the wall is fully functional. The header strip (when `ShowHeader` is on) shows a
small `Trial - N days left` line; the log records the days remaining at every startup, and a
Warning once 7 or fewer days remain.

**The trial marker is bound to this machine.** It is authenticated with an HMAC keyed to a local
machine fingerprint (the same value `--license-fingerprint` prints - see below), so copying
`gridlookout.trial.json` to a different machine to "restart" a trial there does not work: the copy
fails its own authenticity check and that machine simply starts its own fresh trial instead. An
administrator can still delete the marker file to restart a trial on the SAME machine - this is a
known, accepted limitation for 1.0.2 (see the work plan's "Risks"), not a security boundary.

**Clock changes are detected.** If the system clock moves backward by more than about a day
relative to the last time GridLookout ran, the trial is treated as expired rather than
silently extended.

## Buying and installing a licence

Buy at **https://gridlookout.it42.hr/buy**. A licence is **delivered instantly by
email** after payment as a single file, `gridlookout.lic` - no account, no activation step, no
waiting.

To install it on the wall-controller PC, either:

1. **Recommended**: run `GridLookout.exe --license-install <path-to-gridlookout.lic>` from an
   administrator command prompt. It verifies the file and, if valid, copies it into place. A
   wall that is already running picks it up automatically within `ConfigRefreshSeconds` (60
   seconds by default) - **no restart needed**, and the unlicensed banner (if one was showing)
   clears on its own.
2. Copy `gridlookout.lic` by hand next to `camerawall.json` (the writable state directory - see
   above), or set an absolute path to it via the `LicensePath` key in `camerawall.json`, then
   either restart the wall or wait for the next config-refresh tick.

## Checking licence status

```
GridLookout.exe --license
```

Prints the current status, edition, seat count, term, the relevant dates, the licence file path
GridLookout is looking at, and this machine's fingerprint. Exit code `0` when the wall is fully
licensed or on an active trial, `3` otherwise - suitable for a monitoring script.

```
GridLookout.exe --license-fingerprint
```

Prints only this machine's fingerprint (format `XXXXXXXX-XXXXXXXX-XXXXXXXX-XXXXXXXX`) - useful if
a future node-locked licence needs it; ordinary licences today are **licensee-bound, not
machine-bound**, so this is informational unless IT42 issues you a node-locked licence.

## What an unlicensed wall looks like

Once the trial ends (or an installed licence file is invalid, expired, or out of maintenance
coverage for the running build), GridLookout **keeps streaming video** - it never blanks the wall
or stops the process. Two things change:

- A fixed red banner strip appears at the top of every wall window: `UNLICENSED - GridLookout
  <reason>. Buy a licence at gridlookout.it42.hr`. It sits above the header strip (when
  `ShowHeader` is on) and never covers any tile.
- **Layout changes from Management Client are refused** - the wall keeps the `$layout{}` layout it
  had at the moment it became unlicensed, and a refusal is logged once per distinct attempted
  change. A layout already configured at startup, and a config-refresh triggered by a camera
  being added or removed, both keep working - only NEW layout edits made while unlicensed are
  refused. Installing a valid licence lifts the refusal on the next config-refresh tick, no
  restart.

## Air-gap / no phone-home

**Licence verification is entirely local and makes no network connection of any kind.** The
signature check is a local RSA computation against a public key built into the executable; the
trial marker and installed licence file are both read from local disk. GridLookout never checks
in with any server to validate, activate, or report a licence - this holds during the trial, after
buying, and forever after. Air-gapped VMS networks are fully supported for licensing exactly as
they already are for everything else GridLookout does - see `camerawall-security.md`.

## Reference: licence file format

`gridlookout.lic` is a signed JSON envelope (`payload` + `signature`, RSA-3072/SHA-256). The public
key used to verify it ships inside `GridLookout.exe`; the private key never leaves IT42's signing
system. The payload carries the licence id, customer name, edition (`SingleController` or
`FivePack`), seat count, term (`perpetual` or `subscription`), and the relevant dates. Seats are
informational in 1.0.2 - not enforced. The format is intentionally public (see
`camerawall-security.md`) so a technically inclined customer can verify what it checks by reading
the source under `src/GridLookout/Licensing`.
