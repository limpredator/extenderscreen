"""
Stand-in for the iPad client: connects to the ExtendedScreen sender, verifies
the handshake, and prints a summary of the first N access units (NAL types,
sizes, whether each is a keyframe) so we can confirm the wire protocol in
docs/PROTOCOL.md is actually what the sender produces.
"""
import socket
import struct
import sys
import time

HOST = "127.0.0.1"
PORT = 7654
NUM_FRAMES_TO_INSPECT = 40


def recv_exact(sock: socket.socket, n: int) -> bytes:
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("socket closed while reading")
        buf += chunk
    return buf


def nal_types_in(payload: bytes):
    types = []
    i = 0
    n = len(payload)
    while i < n - 3:
        if payload[i] == 0 and payload[i + 1] == 0 and payload[i + 2] == 1:
            start = i + 3
        elif i < n - 4 and payload[i] == 0 and payload[i + 1] == 0 and payload[i + 2] == 0 and payload[i + 3] == 1:
            start = i + 4
        else:
            i += 1
            continue
        if start < n:
            types.append(payload[start] & 0x1F)
        i = start
    return types


def main():
    print(f"Connecting to {HOST}:{PORT} ...")
    sock = socket.create_connection((HOST, PORT), timeout=10)
    sock.settimeout(10)

    magic = recv_exact(sock, 4)
    assert magic == b"EXSC", f"bad magic: {magic!r}"
    version = recv_exact(sock, 1)[0]
    width, height, fps = struct.unpack(">III", recv_exact(sock, 12))
    print(f"Handshake OK: version={version} {width}x{height} @ {fps}fps")

    t0 = time.time()
    total_bytes = 0
    keyframes = 0
    for i in range(NUM_FRAMES_TO_INSPECT):
        msg_type = recv_exact(sock, 1)[0]
        (length,) = struct.unpack(">I", recv_exact(sock, 4))
        payload = recv_exact(sock, length)
        total_bytes += length

        types = nal_types_in(payload)
        is_key = 5 in types
        keyframes += is_key
        print(f"  frame {i:3d}: type=0x{msg_type:02x} len={length:7d} nal_types={types} keyframe={is_key}")

    elapsed = time.time() - t0
    print(f"\nReceived {NUM_FRAMES_TO_INSPECT} access units in {elapsed:.2f}s "
          f"({total_bytes/1024:.1f} KiB total, {keyframes} keyframe(s)). Protocol looks healthy.")
    sock.close()


if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        print(f"FAILED: {e}", file=sys.stderr)
        sys.exit(1)
