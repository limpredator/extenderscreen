namespace ExtendedScreen.Sender;

/// <summary>
/// Splits a continuous Annex-B H.264 byte stream (as emitted by ffmpeg's
/// "-f h264" muxer) into access units: one entry per video frame, each
/// containing every NAL unit that belongs to that frame (e.g. an IDR frame
/// arrives as SPS + PPS + IDR-slice NALs bundled together).
///
/// Assumes one VCL slice NAL per picture (true for libx264's default
/// single-slice-per-frame output, which is what we ask it for). A new access
/// unit is closed out the moment we see the start of the NAL *following* a
/// VCL NAL (type 1 or 5) — everything up to that point belongs to the frame
/// that just finished.
/// </summary>
public sealed class AnnexBAccessUnitSplitter
{
    private readonly List<byte> _buf = new();
    private int _scanPos;
    private int _auStart;
    private bool _haveSeenAnyNal;
    private bool _sawVclInOpenAu;
    private bool _openAuIsKeyFrame;

    /// <summary>Feed newly-read bytes from the ffmpeg pipe. Returns zero or more completed access units.</summary>
    public IEnumerable<(byte[] Data, bool IsKeyFrame)> Feed(ReadOnlySpan<byte> chunk)
    {
        _buf.AddRange(chunk.ToArray());
        var results = new List<(byte[], bool)>();

        while (true)
        {
            var found = FindNextStartCode(_scanPos);
            if (found is null) break;
            var (scStart, scLen) = found.Value;

            int nalDataStart = scStart + scLen;
            if (nalDataStart >= _buf.Count)
            {
                // Start code is right at the tail of what we have; we can't
                // read the NAL header byte yet. Wait for more data.
                break;
            }

            int nalType = _buf[nalDataStart] & 0x1F;
            bool isVcl = nalType == 1 || nalType == 5;
            bool isIdr = nalType == 5;

            if (!_haveSeenAnyNal)
            {
                _auStart = scStart;
                _haveSeenAnyNal = true;
            }
            else if (_sawVclInOpenAu)
            {
                var auBytes = _buf.GetRange(_auStart, scStart - _auStart).ToArray();
                results.Add((auBytes, _openAuIsKeyFrame));
                _auStart = scStart;
                _sawVclInOpenAu = false;
                _openAuIsKeyFrame = false;
            }

            if (isVcl)
            {
                _sawVclInOpenAu = true;
                if (isIdr) _openAuIsKeyFrame = true;
            }

            // Emulation prevention guarantees no start-code-like sequence
            // appears inside a NAL's payload, so it's safe to resume scanning
            // right after this start code.
            _scanPos = nalDataStart;
        }

        // Drop bytes we no longer need (everything before the open access unit).
        if (_auStart > 0)
        {
            _buf.RemoveRange(0, _auStart);
            _scanPos -= _auStart;
            _auStart = 0;
        }

        return results;
    }

    /// <summary>Call once the ffmpeg process exits to flush any trailing access unit.</summary>
    public (byte[] Data, bool IsKeyFrame)? Flush()
    {
        if (_haveSeenAnyNal && _sawVclInOpenAu && _buf.Count > _auStart)
        {
            var auBytes = _buf.GetRange(_auStart, _buf.Count - _auStart).ToArray();
            return (auBytes, _openAuIsKeyFrame);
        }
        return null;
    }

    private (int Offset, int Length)? FindNextStartCode(int from)
    {
        for (int i = from; i + 2 < _buf.Count; i++)
        {
            if (_buf[i] == 0 && _buf[i + 1] == 0)
            {
                if (_buf[i + 2] == 1) return (i, 3);
                if (i + 3 < _buf.Count && _buf[i + 2] == 0 && _buf[i + 3] == 1) return (i, 4);
            }
        }
        return null;
    }
}
