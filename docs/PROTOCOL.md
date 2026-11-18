# ExtendedScreen wire protocol (v1)

LAN-only, no NAT traversal, no auth/encryption in v1 (trusted home network only).
Goal: lowest achievable latency using hardware-accelerated H.264 end to end.

## Transport

Plain TCP. Windows app is the server, tablet is the client.

- Default port: `7654`
- One connection per tablet. Server accepts a new connection and drops any
  previous one (single active viewer per stream for now).

## Handshake (server -> client, sent once right after `accept()`)

```
byte    magic[4]      = "EXSC"
byte    version       = 1
uint32  width         (big-endian, pixels)
uint32  height        (big-endian, pixels)
uint32  fps           (big-endian, target frame rate)
```

13 bytes total after the 4-byte magic (21 bytes total). Client should
validate the magic before trusting the rest.

## Frame stream (after handshake, server -> client, repeats forever)

Every message on the wire (both directions) uses the same envelope:

```
byte    type          (see below)
uint32  length        (big-endian, byte length of payload)
byte    payload[length]
```

### Message types

| type | name         | direction       | payload                                   |
|------|--------------|-----------------|--------------------------------------------|
| 0x01 | VIDEO_FRAME  | server -> client| one H.264 access unit, Annex-B start codes |
| 0x02 | TOUCH_EVENT  | client -> server| see below (not read by server yet, phase 3)|
| 0x03 | PING         | either           | empty, keepalive                           |

`VIDEO_FRAME` payload is exactly what ffmpeg writes to its Annex-B output for
one access unit: it may contain multiple NAL units back to back (e.g. an IDR
frame is sent as `SPS NAL + PPS NAL + IDR slice NAL`, each with its own
`00 00 00 01` start code). The client is responsible for splitting on start
codes and re-muxing into whatever its decoder needs (on iOS: AVCC length-
prefixed NALUs wrapped in a `CMSampleBuffer`, fed to `AVSampleBufferDisplayLayer`).

### TOUCH_EVENT payload (phase 3, defined now so the protocol doesn't change later)

```
byte    phase         0 = began, 1 = moved, 2 = ended, 3 = cancelled
int32   x             (big-endian, normalized 0..1,000,000 across frame width)
int32   y             (big-endian, normalized 0..1,000,000 across frame height)
uint64  timestampMs   (big-endian, client monotonic clock)
```

## Why this shape

- Length-prefixed framing means either side can read exactly one message at a
  time off the socket with no resync logic.
- Annex-B passthrough means the Windows side never has to touch the bitstream
  beyond finding access-unit boundaries — ffmpeg already groups SPS/PPS/IDR
  into one write per frame when using `-bsf:v h264_mp4toannexb` is *not*
  needed (we ask ffmpeg for Annex-B directly via `-f h264`), so the sender is
  just "read a chunk ffmpeg wrote, prefix it, send it."
- No container (no RTP/MPEG-TS/fMP4) because both ends are code we control on
  a single LAN hop — a container buys robustness we don't need yet at the
  cost of latency and complexity.

## Known limitations / future work

- No encryption. Fine on a trusted home LAN; would need TLS before using
  across an untrusted network.
- No Bonjour/mDNS auto-discovery yet — client enters the Windows machine's
  LAN IP manually. Easy to add later (`NWBrowser` on iOS, a small UDP/mDNS
  responder on Windows) without changing this protocol.
- Single viewer at a time.
- `TOUCH_EVENT` is defined but not yet consumed by the server (needs
  `SendInput` wiring on Windows — phase 3).
