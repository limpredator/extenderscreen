import Foundation
import VideoToolbox
import CoreMedia

/// Turns the Annex-B access units coming off `StreamClient` into
/// `CMSampleBuffer`s ready for `AVSampleBufferDisplayLayer`.
///
/// Hardware decoding itself is handled by `AVSampleBufferDisplayLayer`
/// internally (it uses VideoToolbox under the hood) — this class only does
/// the bitstream bookkeeping: splitting Annex-B into NAL units, building the
/// `CMVideoFormatDescription` from SPS/PPS, and converting each slice NAL
/// into the length-prefixed AVCC form `CMSampleBuffer` expects.
final class H264StreamDecoder {
    private var formatDescription: CMVideoFormatDescription?
    private var spsData: Data?
    private var ppsData: Data?

    func makeSampleBuffer(fromAnnexB accessUnit: Data) -> CMSampleBuffer? {
        let nalUnits = splitAnnexB(accessUnit)
        var sliceData: Data?
        var sawNewParameterSets = false

        for nal in nalUnits {
            guard let first = nal.first else { continue }
            switch first & 0x1F {
            case 7: spsData = nal; sawNewParameterSets = true
            case 8: ppsData = nal; sawNewParameterSets = true
            case 1, 5: sliceData = nal
            default: break
            }
        }

        if sawNewParameterSets || formatDescription == nil {
            if let sps = spsData, let pps = ppsData {
                formatDescription = makeFormatDescription(sps: sps, pps: pps)
            }
        }

        guard let sliceData, let formatDescription else { return nil }
        return makeSampleBuffer(forSlice: sliceData, formatDescription: formatDescription)
    }

    /// Splits one Annex-B access unit into its constituent NAL units
    /// (start-code bytes stripped). Mirrors AnnexBAccessUnitSplitter on the
    /// Windows side, just operating on an already-complete access unit
    /// rather than a live byte stream.
    private func splitAnnexB(_ data: Data) -> [Data] {
        let bytes = [UInt8](data)
        var markers: [(scStart: Int, nalStart: Int)] = []

        var i = 0
        while i + 2 < bytes.count {
            if bytes[i] == 0, bytes[i + 1] == 0, bytes[i + 2] == 1 {
                markers.append((i, i + 3))
                i += 3
            } else if i + 3 < bytes.count, bytes[i] == 0, bytes[i + 1] == 0, bytes[i + 2] == 0, bytes[i + 3] == 1 {
                markers.append((i, i + 4))
                i += 4
            } else {
                i += 1
            }
        }

        var nals: [Data] = []
        for (idx, marker) in markers.enumerated() {
            let nalEnd = idx + 1 < markers.count ? markers[idx + 1].scStart : bytes.count
            if marker.nalStart < nalEnd {
                nals.append(Data(bytes[marker.nalStart..<nalEnd]))
            }
        }
        return nals
    }

    private func makeFormatDescription(sps: Data, pps: Data) -> CMVideoFormatDescription? {
        var formatDesc: CMVideoFormatDescription?

        let status = sps.withUnsafeBytes { spsRaw -> OSStatus in
            pps.withUnsafeBytes { ppsRaw -> OSStatus in
                let spsPtr = spsRaw.bindMemory(to: UInt8.self).baseAddress!
                let ppsPtr = ppsRaw.bindMemory(to: UInt8.self).baseAddress!
                let pointers: [UnsafePointer<UInt8>] = [spsPtr, ppsPtr]
                let sizes: [Int] = [sps.count, pps.count]
                return pointers.withUnsafeBufferPointer { pointerBuf in
                    sizes.withUnsafeBufferPointer { sizeBuf in
                        CMVideoFormatDescriptionCreateFromH264ParameterSets(
                            allocator: kCFAllocatorDefault,
                            parameterSetCount: 2,
                            parameterSetPointers: pointerBuf.baseAddress!,
                            parameterSetSizes: sizeBuf.baseAddress!,
                            nalUnitHeaderLength: 4,
                            formatDescriptionOut: &formatDesc)
                    }
                }
            }
        }

        return status == noErr ? formatDesc : nil
    }

    private func makeSampleBuffer(forSlice slice: Data, formatDescription: CMVideoFormatDescription) -> CMSampleBuffer? {
        var bigEndianLength = UInt32(slice.count).bigEndian
        var avcc = Data(bytes: &bigEndianLength, count: 4)
        avcc.append(slice)

        var blockBuffer: CMBlockBuffer?
        var status = CMBlockBufferCreateWithMemoryBlock(
            allocator: kCFAllocatorDefault,
            memoryBlock: nil,
            blockLength: avcc.count,
            blockAllocator: kCFAllocatorDefault,
            customBlockSource: nil,
            offsetToData: 0,
            dataLength: avcc.count,
            flags: 0,
            blockBufferOut: &blockBuffer)
        guard status == kCMBlockBufferNoErr, let blockBuffer else { return nil }

        status = avcc.withUnsafeBytes { raw in
            CMBlockBufferReplaceDataBytes(
                with: raw.baseAddress!,
                blockBuffer: blockBuffer,
                offsetIntoDestination: 0,
                dataLength: avcc.count)
        }
        guard status == kCMBlockBufferNoErr else { return nil }

        var sampleBuffer: CMSampleBuffer?
        var timing = CMSampleTimingInfo(
            duration: .invalid,
            presentationTimeStamp: CMClockGetTime(CMClockGetHostTimeClock()),
            decodeTimeStamp: .invalid)
        var sampleSize = avcc.count

        status = CMSampleBufferCreateReady(
            allocator: kCFAllocatorDefault,
            dataBuffer: blockBuffer,
            formatDescription: formatDescription,
            sampleCount: 1,
            sampleTimingEntryCount: 1,
            sampleTimingArray: &timing,
            sampleSizeEntryCount: 1,
            sampleSizeArray: &sampleSize,
            sampleBufferOut: &sampleBuffer)
        guard status == noErr, let sampleBuffer else { return nil }

        let isKeyFrame = (slice.first ?? 0) & 0x1F == 5
        if let attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: true) {
            let dict = unsafeBitCast(CFArrayGetValueAtIndex(attachments, 0), to: CFMutableDictionary.self)
            if !isKeyFrame {
                CFDictionarySetValue(
                    dict,
                    Unmanaged.passUnretained(kCMSampleAttachmentKey_NotSync).toOpaque(),
                    Unmanaged.passUnretained(kCFBooleanTrue).toOpaque())
            }
            // Low-latency: present this frame the instant it's decoded
            // instead of letting AVSampleBufferDisplayLayer schedule it
            // against the presentation timestamp (which defaults to "now"
            // anyway here, but DisplayImmediately removes any internal
            // scheduling slack on top of that).
            CFDictionarySetValue(
                dict,
                Unmanaged.passUnretained(kCMSampleAttachmentKey_DisplayImmediately).toOpaque(),
                Unmanaged.passUnretained(kCFBooleanTrue).toOpaque())
        }

        return sampleBuffer
    }
}
