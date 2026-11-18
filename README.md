# ExtendedScreen

A custom extended-display app: a Windows sender that streams hardware-encoded
H.264 video over your LAN, and native viewer apps (iPad and Android/Samsung
Tab) that decode and display it full-screen. Built as an alternative to
Superwired Display that can target both an iPad and your Samsung tablet from
one Windows app.

## Day-to-day usage

The Windows sender is a **tray app**, not a command you type. Double-click
`ExtendedScreen.exe` (or the desktop shortcut) — it auto-detects your
extended/virtual monitor, picks a sensible resolution/fps/bitrate for it, and
starts streaming immediately, with no console window and no flags. It lives
in the system tray; right-click the icon for Start/Stop/Show log/Exit.

It also keeps retrying USB setup (`adb reverse`) every few seconds for as
long as it's running, so the order doesn't matter — open the app first and
plug the tablet in later, or the other way around, either way "Connect via
USB" on the tablet just works within a few seconds, no restart needed.

On the tablet: open the **ExtendedScreen** app, tap **Connect via USB** (or
type the PC's IP for Wi-Fi). That tap is still manual — nothing currently
auto-launches or auto-connects the tablet side the instant the cable goes in.

The original command-line mode still works unchanged for scripting/debugging
(`ExtendedScreen.Sender.exe --source desktop --monitor 1 --fps 60 --bitrate 10000`)
— passing any argument switches it from tray mode to the old console mode.

Both client apps share one dark, minimal UI: a rounded card to type the PC's
IP once (remembered after that), then a clean fullscreen video view with a
small tap-to-disconnect button — no menus, no settings screens to get lost
in. The Android client also has a **"Connect via USB"** button: plug the
tablet into the PC with a USB cable and tap it — no IP address needed, works
on any network (or none), and the Windows sender sets up the forwarding
automatically. See "USB mode" below.

See [docs/PROTOCOL.md](docs/PROTOCOL.md) for the exact wire format.

## Status

- [x] Wire protocol defined
- [x] Windows sender: streams a synthetic test pattern (`--source mock`) *or*
      mirrors one of your real monitors (`--source desktop`, via ffmpeg's
      `gdigrab` — no driver install needed, but this **mirrors** an existing
      display, it doesn't add a new one)
- [x] Verified end-to-end with a throwaway Python client (`test_client.py`
      at the repo root) — handshake, keyframe bundling, and frame framing
      all check out
- [x] iPad SwiftUI client written (VideoToolbox decode via
      `AVSampleBufferDisplayLayer`, manual IP entry)
- [ ] **Not yet built/run on an actual iPad** — I don't have access to a Mac
      or iPad from this environment, so the Swift code below is untested.
      Build it in Xcode and tell me what breaks.
- [x] Android client for the Samsung Tab — **built, installed, and verified
      live on a real Galaxy Tab S8**, including real desktop mirroring
      (`--source desktop`), not just the test pattern: the actual Windows
      desktop renders full-screen, sharp, and correctly colored. This is the
      most-verified client of the three, by a wide margin.
- [x] True "extend" (a brand-new virtual display, not a mirror of an existing
      one) — **built and verified**, using a third-party virtual display
      driver instead of writing our own. See "Extend mode" below.
- [ ] Touch/pencil input passthrough (phase 3, protocol already reserves a
      message type for it)
- [x] USB connect mode for Android — **built and verified**: plug in, tap
      "Connect via USB", no IP typing, works regardless of which Wi-Fi
      network (if any) either device is on
- [ ] Auto-discovery (Bonjour/mDNS) — right now you type the PC's IP by hand
      once per device; it's remembered after that, but a zero-typing "just
      shows up" experience is the natural next step toward plug-and-play.
- [x] Windows sender as a **tray app** — **built and verified**: no console,
      no flags, auto-detects the virtual monitor and picks fps/bitrate for
      it, starts streaming on open, retries USB setup continuously in the
      background. See "Day-to-day usage" above.

## Repo layout

```
docs/PROTOCOL.md        wire protocol spec
windows-sender/         .NET console app (the "server")
ipad-client/            SwiftUI source files for the iPad app (the "client")
android-client/         Android Studio / Gradle project for the Android app (the "client")
test_client.py          throwaway protocol-verification script, not part of the product
```

## Running the Windows sender

Requires the **.NET 9 SDK** and **ffmpeg** on PATH (both already set up if
you're continuing on this machine; otherwise `winget install
Microsoft.DotNet.SDK.9` and `winget install ffmpeg`).

**Easiest way (no terminal needed):** double-click
[`windows-sender/Start-ExtendedScreen.bat`](windows-sender/Start-ExtendedScreen.bat).
It launches the pre-built `publish\ExtendedScreen.Sender.exe` — a
self-contained single-file executable (no .NET install required on the
machine running it) — mirroring your primary monitor at sensible defaults.
If you edit the sender's source, rebuild that exe with:

```bash
cd windows-sender
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

For development/testing with custom options, run it directly instead:

```bash
cd windows-sender
dotnet run -- --source mock --width 1280 --height 720 --fps 30
```

It prints the LAN IPs to type into the iPad app, then waits for a connection
on port 7654. `--source mock` draws a test pattern with a clock + frame
counter overlay — useful for eyeballing latency once the iPad app is
connected (compare the on-screen clock to a real one). To mirror a real
monitor instead:

```bash
dotnet run -- --source desktop --monitor 0 --fps 60 --bitrate 12000
```

Run it once with a bad `--monitor` index to print the detected monitor list.

**Important path-length note:** if you ever move this project onto a very
deeply-nested folder path again, Windows' classic 260-character path limit
can make `dotnet build` fail in a confusing way (it'll compile fine but the
final copy step reports the .dll as "not found" even though it exists). Keep
the project on a short path like `C:\Users\<you>\ExtendedScreen\` and it
won't come up.

## Building the iPad app

I can't run Xcode from here (it's macOS-only and this is a Windows machine),
so this is source-only — you'll assemble the actual Xcode project:

1. On your Mac, open Xcode → **File → New → Project** → iOS → **App**.
   Name it `ExtendedScreen`, interface **SwiftUI**, language **Swift**.
2. Delete the template's `ContentView.swift` and `ExtendedScreenApp.swift`
   (or just overwrite them).
3. Drag all the files from this repo's `ipad-client/` folder into the Xcode
   project navigator (check "Copy items if needed"):
   - `ExtendedScreenApp.swift`
   - `ContentView.swift`
   - `StreamViewModel.swift`
   - `StreamClient.swift`
   - `H264StreamDecoder.swift`
   - `VideoDisplayView.swift`
4. Add the Info.plist entry described in
   [ipad-client/Info-additions.md](ipad-client/Info-additions.md) (local
   network usage description — iOS prompts the user for this on first
   connect).
5. Plug in your iPad, select it as the run destination, and hit Run. Xcode
   will ask you to trust your developer certificate on the iPad the first
   time (Settings → General → VPN & Device Management) — that's the
   "sideload via dev account" step, no App Store involved.
6. Make sure the iPad and the Zenbook are on the **same Wi-Fi network**, run
   the Windows sender, note the IP it prints, type it into the iPad app, tap
   Connect.

### What to check if it doesn't work

- Nothing happens / immediate error → check the IP and that the Windows
  firewall isn't blocking port 7654 (Windows will likely prompt the first
  time the sender runs; allow it on Private networks).
- iOS never shows the local-network permission prompt / connection times out
  → that's the most likely rough edge, since I couldn't test the actual
  iOS networking/permission flow. Check Settings → Privacy & Security →
  Local Network → ExtendedScreen is enabled.
- Connects but shows a black/frozen screen → almost certainly in
  `H264StreamDecoder.swift`, the one file with no way for me to verify
  correctness without a real decode pipeline to test against. Worth
  dropping print statements around `makeFormatDescription`/
  `makeSampleBuffer`'s status codes first.

## Building/installing the Android app (Samsung Tab)

Unlike the iPad side, this one I could actually build end-to-end here: the
`android-client/` folder is a real Gradle project that compiles to a working
`.apk` (`android-client/app/build/outputs/apk/debug/app-debug.apk` after a
build). To rebuild it:

```bash
cd android-client
.\gradlew.bat assembleDebug
```

To install it on your Samsung Tab:

1. On the tablet: **Settings → About tablet → tap "Build number" 7 times**
   to unlock Developer Options, then **Settings → Developer options → enable
   USB debugging**.
2. Plug the tablet into the Zenbook via USB, accept the "Allow USB
   debugging?" prompt on the tablet.
3. From `android-client/`, run:
   ```bash
   C:\Users\USER\AppData\Local\Android\Sdk\platform-tools\adb.exe install -r app\build\outputs\apk\debug\app-debug.apk
   ```
   (Or just copy the `.apk` file to the tablet — e.g. via a USB cable, email,
   or a cloud drive — and open it there; Android will ask to allow installs
   from that source the first time.)
4. Open "ExtendedScreen" on the tablet, make sure it's on the same Wi-Fi as
   the Zenbook, run the Windows sender, type in the IP it prints, tap
   Connect.

### What actually happened getting this working (worth reading before touching the iOS decoder)

This client went through real on-device debugging, not just a compile check,
and the bug that cost the most time is the kind that could easily bite the
iPad client too if it's ever modified — so it's worth understanding even if
you never touch Android.

**The bug:** `MediaCodec` requires `csd-0`/`csd-1` (the SPS/PPS "codec
specific data" passed at `configure()` time) to be in **Annex-B form — with
the 4-byte `00 00 00 01` start code prepended**. Our NAL-splitting helper
strips start codes when it locates NAL ranges (so slice data can be
re-framed cleanly), and the SPS/PPS extraction reused those same stripped
ranges. Result: `MediaCodec` accepted input forever, reported `RUNNING`,
and `releaseOutputBuffer` never threw — but it never produced a single
output frame, because it never actually got a valid configuration. No
exception, no log line, nothing — just a silently-wedged decoder. Confirmed
by instrumenting every stage (input callbacks firing, output callbacks
never firing) before finding it.

This single bug was the entire reason the screen stayed black through a long
chain of red herrings that all turned out to be irrelevant once it was
fixed: `SurfaceView` Z-ordering, `TextureView` vs `SurfaceView`, hardware vs
software decoder, Samsung DeX/freeform windowing, even a manual YUV→RGB
software-rendering fallback — none of those were the actual problem, and all
of that complexity was reverted once the real fix landed. The final, simple,
efficient version is what's in this repo now: hardware `MediaCodec` decoding
straight to a `TextureView`'s `Surface`, exactly the standard pattern.

**Does the iPad decoder have the same bug?** No — checked specifically,
because this was asked about directly. `H264StreamDecoder.swift` calls
`CMVideoFormatDescriptionCreateFromH264ParameterSets`, and Apple's contract
for that function is the opposite of Android's: it wants the **raw NAL
bytes, without start codes**, which is exactly what our NAL-splitting
produces there. So the iOS side was already correct — this is a case where
the two platforms' decoder APIs genuinely disagree about the data format,
not a bug that ported over.

**What did carry over to iOS:** a real latency improvement. Every sample
buffer now carries `kCMSampleAttachmentKey_DisplayImmediately`, which tells
`AVSampleBufferDisplayLayer` to present the frame the instant it's decoded
rather than scheduling it against its presentation timestamp — removes a
layer of internal buffering/scheduling slack that isn't needed for a
same-LAN low-latency stream.

### The second bug: real desktop mirroring looked "mostly green"

Getting the test pattern working wasn't the end of it. Switching to real
desktop mirroring (`--source desktop`) produced a consistent, reproducible
artifact on the Android client: only the top ~10% of the frame ever showed
real pixels, and the rest was a flat, uniform green. This reproduced with
the synthetic test pattern too once the resolution matched the desktop
capture, with the software decoder instead of the hardware one, after a full
tablet reboot, and from a freshly-started sender — which ruled out content,
resolution, vendor HAL state, and connection-age as the cause one at a time.

**The actual cause:** `-tune zerolatency` disables libx264's frame-level
multithreading (buffering several frames ahead would add latency), but to
still use a multi-core CPU, it falls back to **slice-level** threading
instead — splitting every encoded picture into one VCL slice NAL per thread.
On this machine that meant **11 separate slice NAL units per frame**,
confirmed by decoding the actual slice headers (`first_mb_in_slice` stepping
by 320 across 11 consecutive NALs, then resetting to 0 for the next
picture). Every piece of code in this repo — the Windows-side
`AnnexBAccessUnitSplitter`, and both the Android and iOS decoders — assumed
exactly one VCL NAL per picture. Each slice was therefore being delivered
and decoded as if it were an entire independent frame: whichever rows that
one slice actually covered got real pixels, and the rest of the picture was
left however the decoder's output buffer already looked (a solid green fill
on this device). Setting x264's own `slices=1` param didn't stick — ffmpeg's
libx264 wrapper kept slice-threading anyway — so the fix is blunter and
reliable: `-threads 1` on the ffmpeg command, which removes the
multi-threading that triggered slice-splitting in the first place.

This was entirely a Windows-sender bug, not a per-client one: the bitstream
itself had multiple slices per frame, so it would have broken the iOS
decoder identically whenever it's finally tested on real hardware. Fixing it
in `windows-sender/Program.cs` fixes it for every client at once.

## USB mode

Typing the PC's LAN IP works at home, but it breaks the moment either device
moves to a different network (a different Wi-Fi, a phone hotspot, etc.) — the
IP changes and you have to go find the new one. USB mode sidesteps that
entirely: the Android client's **"Connect via USB"** button always connects
to `127.0.0.1:7654`, and on the PC side `Program.cs` runs
`adb reverse tcp:7654 tcp:7654` automatically at startup, which makes the
tablet's own `127.0.0.1:7654` transparently forward over the USB cable to the
PC's real `7654`. From the tablet's point of view there's no network at all —
just the cable — so it works identically at home, at the office, anywhere,
with zero reconfiguration.

This needs `adb` (installed with Android Studio / the Android SDK
platform-tools) and USB debugging enabled on the tablet, authorized for this
PC. The sender looks for `adb` on PATH first, then falls back to the default
SDK install location
(`%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe`) if PATH doesn't have
it. If neither works it just prints a one-line notice and carries on — Wi-Fi
mode (typing the IP) is unaffected either way, USB mode just won't be
available until `adb` can be found or a device gets authorized.

Verified end-to-end on the Galaxy Tab S8: plugged in over USB, tapped
"Connect via USB" with no IP typed anywhere, and the real desktop mirror
rendered full-screen immediately.

## Extend mode

`--source desktop` by itself only **mirrors** a real monitor — gdigrab is
just a screen-grab, it can't make Windows see a display that doesn't exist.
True "extend" (a separate monitor you can drag windows onto, independent
content, shows up in Settings → Display) needs an actual kernel-mode virtual
display driver registered with Windows. Writing one from scratch is a
large undertaking (WDF/IddCx, driver signing); instead we use an existing
open-source one and just point the sender at the monitor it creates.

**Driver used:** [VirtualDrivers/Virtual-Display-Driver](https://github.com/VirtualDrivers/Virtual-Display-Driver)
(the project formerly/also known as MttVDD). Signed, actively maintained,
supports custom resolutions up to 4K and refresh rates up to 165Hz.

### The virtual monitor only exists while something's watching it

Early on, the virtual monitor behaved like a permanently-extended dead
screen: disconnecting the tablet left it fully attached to the desktop, so
the mouse cursor could wander onto it and windows dragged there just
vanished from view with nothing to show them. Fixed in `SenderService.cs` /
`DisplayConfig.cs`: the monitor is attached (`DisplayConfig.Attach`) the
moment a viewer connects, and detached (`DisplayConfig.Detach`) the moment
it disconnects — same experience as unplugging a real monitor's cable.
ffmpeg capture itself is tied to the same lifecycle (only runs while
something's connected), since detaching mid-capture would otherwise break
gdigrab's already-open capture region.

Two things had to be exactly right for this to be safe:

1. **Finding the actual virtual monitor, not just "the other screen".**
   `Screen.AllScreens.FirstOrDefault(s => !s.Primary)` works at home with
   one real monitor, but breaks the moment there's a second *real* monitor
   around (confirmed live at an office desk: it picked the real Samsung
   monitor as the stream target, which would have streamed real desktop
   content to the tablet and then detached a real monitor on disconnect).
   Fixed in `VirtualMonitorFinder.cs`: finds the adapter by its actual
   identity via `EnumDisplayDevices` (`DeviceString == "Virtual Display
   Driver"`), not by a primary/non-primary guess. If that adapter isn't
   currently attached to the desktop, falls back to mirroring the primary
   screen with a warning rather than guessing wrong.
2. **Never touching whichever monitor happens to be Windows' primary.**
   Also found live: the virtual monitor had somehow become the Windows
   *primary* display (a leftover from earlier driver reinstalls), which
   meant real foreground windows were rendering on it and got streamed to
   the tablet. `IsPrimaryDevice` on `Options` gates every attach/detach
   call — if the identified monitor is primary, auto attach/detach is
   skipped entirely and a warning is logged, rather than ever detaching
   whatever Windows currently considers primary. The actual fix for that
   specific mixup is setting your real monitor as primary in Display
   Settings, not code — the code's job here is only to never make a bad
   situation worse.

Also worth knowing: the officially-documented technique for detaching a
monitor via `ChangeDisplaySettingsEx` — passing a NULL `DEVMODE` — reports
success but doesn't actually detach anything on this driver. What actually
works is passing a real `DEVMODE` with `dmPelsWidth`/`dmPelsHeight`
explicitly zeroed. See `DisplayConfig.Detach`.

### Setup (one-time)

1. Download the driver package from the project's
   [releases page](https://github.com/VirtualDrivers/Virtual-Display-Driver/releases).
2. Run `installCert.bat` as administrator (installs the signing certificate).
3. Device Manager → right-click your PC name → **Action → Add legacy
   hardware** → "Install the hardware that I manually select from a list" →
   **Display adapters** → **Have Disk** → point at `MttVDD.inf` in the
   extracted folder.
4. **Copy `vdd_settings.xml` to `C:\VirtualDisplayDriver\vdd_settings.xml`**
   (create the folder if it doesn't exist). The driver reads its resolution
   list from this exact path — if you install the driver manually (step 3)
   instead of through their own installer app, this file doesn't get placed
   automatically and the virtual monitor won't show up at all.
5. In Device Manager, disable and re-enable the "Virtual Display Driver"
   device once so it picks up the settings file.
6. Run `DisplaySwitch.exe /extend` (or just open Settings → Display, the
   new monitor shows up there too) to actually attach it to the desktop —
   installing the driver alone doesn't extend onto it.

After that it behaves like any other monitor: Settings → Display → select
it → pick a resolution from the dropdown → **Keep changes**. Changing it
through Settings persists properly; scripting it via `ChangeDisplaySettingsEx`
does not (it silently reverts to the default 800x600 the next time the
device is queried) — use the Settings app, not a script, for this.

### Pointing the sender at it

Find its index the same way as any monitor (`Screen.AllScreens`, or run the
sender with a deliberately-bad `--monitor` index to print the detected
list). On this machine it came up as index 1:

```
ExtendedScreen.Sender --source desktop --monitor 1 --fps 60 --bitrate 35000
```

**Verified settings, in order of what was actually tested:** 800x600 (driver
default) → 1920x1080 → 2560x1440 → **3840x2160 @ 60fps, 35 Mbps**, all
confirmed rendering sharp on the Galaxy Tab S8 with zero dropped frames.
ffmpeg never used more than ~1.3 of this machine's 14 logical cores at any
of these settings — even at 4K60 there's substantial CPU headroom, so 4K is
the practical ceiling (the driver doesn't offer anything higher), not a
performance compromise.

## Why things are shaped this way

- **H.264 over a raw length-prefixed TCP stream, not WebRTC.** WebRTC is
  built for untrusted networks with NAT traversal, congestion control, and
  encryption baked in — overhead you want for the open internet, not for one
  trusted device talking to another over the same Wi-Fi network. Hardware
  encode (ffmpeg, `zerolatency` tuning) + hardware decode
  (`AVSampleBufferDisplayLayer`) gets you the actual latency win without
  implementing a full ICE/SDP/SRTP stack by hand.
- **ffmpeg as a subprocess, not a native Media Foundation/DirectX encoder
  binding.** Hand-rolling COM interop for Media Foundation's H.264 encoder is
  a large source of hard-to-debug bugs, especially without being able to
  watch it run on real hardware here. ffmpeg's `libx264 -tune zerolatency` is
  mature, well-understood, and easy to swap for a hardware encoder
  (`h264_nvenc`, `h264_qsv`, `h264_mf`) later by changing one argument.
- **Manual IP entry instead of Bonjour**, for now — fewer moving parts to get
  the first connection working; auto-discovery is a natural phase-2 addition
  that doesn't require changing the wire protocol.
