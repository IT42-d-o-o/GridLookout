# Compatibility Matrix

## Milestone XProtect

| VMS | Status |
|---|---|
| XProtect 2026 R1 (26.1) | **Tested** — live pass 2026-09-01: MIP SDK login, configuration read and live JPEG streaming from a 2026 R1 Recording Server (`26.1.19310.1`), Windows authentication; regression 2026-09-01 with build 1.0.2: Recording Server restart recovered in 26 s, stalled-feed detection in 15 s, health-probe verdicts 0/1/3 verified |
| XProtect 2025 R3 (25.3) | **Tested** — development and continuous live testing |
| XProtect 2023 R1 – 2025 R2 | Expected to work (MIP SDK compatibility window) — not yet verified |
| Older than 2023 R1 | Unknown — not tested |

Built against MIP SDK **25.2.3**.

**Recording Server encryption ("encryption to clients and servers") is supported — tested live
2026-09-01.** With streaming-media encryption enabled on a 2025 R3 Recording Server (Server
Configurator, certificate issued by the site's own CA), GridLookout negotiated TLS 1.3 to port 7563
with full certificate-chain validation and streamed live tiles; with encryption disabled the same
port serves the plain ImageServer protocol. No GridLookout setting is involved — the MIP SDK follows
the recorder's configuration. The only requirement is that the wall machine trusts the certificate
authority that issued the recorder's certificate (Windows certificate store, LocalMachine or
CurrentUser Trusted Root).

**Designed to work on every XProtect edition — including Express+ and Professional+.** The
application only needs login, configuration read, and live JPEG streams — capabilities present
across all editions per Milestone's own MIP/VMS support documentation; it does not use Smart Wall
and needs no Smart Wall / Expert / Corporate licensing. Edition coverage beyond the tested row
above is a design expectation, not a per-edition validation — only the **Tested** row has been
verified in this lab. The XProtect user it logs in with needs a role with live-view permission on
the displayed cameras.

## Wall machine (where the application runs)

| Component | Requirement |
|---|---|
| OS (tested) | Windows 11 x64 |
| OS (expected) | Windows 10 x64 1903+ and Windows Server 2022+ (.NET Framework 4.8 preinstalled); Windows 10 1809 / Server 2019 after installing .NET Framework 4.8 |
| Architecture | x64 only |
| Runtime | .NET Framework 4.8 — in-box on Windows 10 1903+/Windows 11/Server 2022+ (**no runtime installation needed there**); Windows 10 1809 / Server 2019 need the .NET Framework 4.8 feature installed first, per the OS row above |
| GPU | None required — JPEG decoding is CPU-side; any display output works |
| Memory | Measured: 401 MB working set at 4 large (near-fullscreen) tiles, 2026-08-18; ≈170 MB flat from 4 through 20 tiles in a 1280×720 window, 2026-08-19 — with `FitFrameSizeToTile` (default), memory tracks total decoded frame area, not tile count. Recording-server CPU measured statistically flat (5.3–6.6% VM average) from 4 to 20 concurrent 12 fps JPEG streams. Page/tile rotation further reduces concurrent streams. **Method caveat:** single-lab observations (one development workstation driving virtualized lab recorders with synthetic 12 fps JPEG sources) — useful sizing guidance, not capacity guarantees; validate against your own camera resolutions, frame rates, and hardware during a pilot. |
| Network | Reachability of the Management Server and the recorder's Recording Server (default TCP 7563); LAN recommended for full-frame-rate walls |

## Verification status

Rows marked *Tested* are exercised against a live XProtect system on every release. Rows
marked *Expected* follow from platform/SDK compatibility guarantees but have not had a live
pass — treat them as supported-on-paper until listed as tested.
