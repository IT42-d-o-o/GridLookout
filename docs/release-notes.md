# GridLookout Release Notes

The repository has no root `CHANGELOG` - release notes for the `src/GridLookout` product live
here.

## 1.0.2 (rebuilt 2026-09-27)

This rebuild reuses the version number 1.0.2 rather than incrementing the minor version (owner
decision, 2026-09-26), because 1.0.2 is the version Milestone verified on 2026-09-25. The
`GridLookout-1.0.2.msi` published on 22 August 2026 was missing the NVIDIA CUDA runtime DLLs
(see the "Fix" item below) and crash-looped on any machine without one on its PATH; that asset has
been replaced by this build - compare the SHA-256 on the release. **WiX's `MajorUpgrade` will not
upgrade a machine that already has the 22 August 1.0.2 installed to this rebuild** - uninstall it
first, then install this one.

**Memory**: on a machine with an NVIDIA GPU the MIP SDK media toolkit created a CUDA context per live
source although GridLookout decodes the JPEG frames itself and never uses that pipeline. New key
`HardwareDecoding` (default `Off`) hides the GPU from the GridLookout process before the SDK starts and
asks the toolkit for software decoding; `SdkDecodingThreads` (default `"1,1"`) trims its idle decode
threads. On a 20-tile wall this cut private memory 150 s after start from 1.2 GB to 0.57 GB with an
identical picture (issue #12). The toolkit still opens a Direct3D device per live source, and the
NVIDIA driver spends about 25 threads per device (703 threads on that wall); `install-kiosk.ps1` now
pins the exe to the integrated GPU for the kiosk user by default (`-KeepGpuPreference` to skip),
which took the same wall to 262 threads and 369 MB.

**Licensing**: GridLookout now runs a full-featured, automatic 30-day trial on first run, then
keeps streaming with a red "UNLICENSED" banner and refuses new `$layout{}` layout changes from
Management Client until a licence file (`gridlookout.lic`) is installed. Licence verification is
entirely local (RSA-3072/SHA-256 signature check against a public key built into the executable) -
no network connection, no activation server, ever; see `docs/gridlookout-licensing.md` for the
full mechanics and `docs/camerawall-commercial-license.md` for how to buy and install one. Three
new command-line switches: `--license`, `--license-install <path>`, `--license-fingerprint`. MSI
ships unsigned for now.

**Fix (disabled-camera tile stability)**: a plain ordinal reference (`Monitors[].Cameras` ranges/
`"all"`, or a bare digit in `$layout{}`) used to index the enabled-camera list only, so disabling a
camera in Management Client silently removed its tile from the grid and shifted every later
camera's ordinal up one slot. Ordinals now index the full camera catalog (enabled and disabled) by
default — a disabled camera keeps its slot and shows the same dark UNAVAILABLE `(disabled)`
placeholder an alias/guid reference to it already did, and no other camera's ordinal moves. New
`HideDisabledCameras` config key (default `false`) restores the old enabled-only behaviour for a
site that disables a camera specifically to hide it from the wall. See
`docs/camerawall-admin-guide.md`'s "A camera disabled..." paragraph.

**Fix (cudart)**: restored `cudart64_12.dll`, `nppc64_12.dll`, and `nppig64_12.dll` to the shipped payload.
The 2026-08-18 unused-SDK-payload trim had deleted all three as apparently-unused NVIDIA CUDA/NPP
runtime libraries; the MIP SDK's native `CoreToolkits.dll` actually delay-loads `cudart64_12.dll`
the moment it initialises a JPEG live source - on every machine, GPU or not. With the file absent,
the VC++ delay-load helper raised `0xC06D007E` inside the SDK: every tile reported "Could not open
file 'CoreToolkits.dll'!" and never framed, with a Windows Error Reporting crash report every few
seconds. Every MSI install on a machine without a stray `cudart64_12.dll` already on `PATH` was
affected; the development workstation had hidden the defect because unrelated HP OMEN software
puts one on the system `PATH`. Root-caused with a Process Monitor capture on a GPU-less lab VM and
confirmed by dropping the three files back in next to a running (previously broken) wall, which
began rendering immediately with no restart. `docs/gridlookout-NOTICES.md` lists the NVIDIA
runtime again; `GridLookout.csproj`'s `TrimUnusedSdkPayload` target now carries a permanent comment
naming all three as required, not unused.
