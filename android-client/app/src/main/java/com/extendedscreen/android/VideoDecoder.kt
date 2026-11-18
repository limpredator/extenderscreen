package com.extendedscreen.android

import android.media.MediaCodec
import android.media.MediaFormat
import android.util.Log
import android.view.Surface
import java.nio.ByteBuffer

/**
 * Feeds Annex-B H.264 access units (as delivered by [StreamClient]) into a
 * hardware-accelerated [MediaCodec] decoder that renders straight to a
 * [Surface] — the efficient path; MediaCodec's internal pipeline handles
 * YUV→RGB and composition onto the Surface for free.
 *
 * csd-0/csd-1 (SPS/PPS) must be handed to MediaFormat in Annex-B form, i.e.
 * WITH the 4-byte start code prepended — this is the actual contract for
 * AVC's codec-specific data, independent of whether input NAL units
 * themselves need start codes. Passing them without the start code (the
 * original bug here) makes the decoder accept input and report RUNNING
 * forever while never producing a single output frame, with no error —
 * confirmed by instrumenting every stage of the pipeline on a real device.
 */
class VideoDecoder(private val surface: Surface) {

    private var codec: MediaCodec? = null
    private var configured = false
    private var spsData: ByteArray? = null
    private var ppsData: ByteArray? = null
    private var width = 0
    private var height = 0

    fun setExpectedSize(width: Int, height: Int) {
        this.width = width
        this.height = height
    }

    /**
     * Called from the StreamClient callback thread. Synchronized against
     * [release], which runs on the UI thread (surface destroyed / activity
     * torn down) and can otherwise race a frame that's mid-decode.
     */
    @Synchronized
    fun feed(accessUnit: ByteArray) {
        val nalUnits = splitAnnexB(accessUnit)
        var sliceStart = -1
        var sliceEnd = -1

        for (nal in nalUnits) {
            if (nal.start >= nal.end) continue
            when (accessUnit[nal.start].toInt() and 0x1F) {
                7 -> spsData = withStartCode(accessUnit, nal.start, nal.end)
                8 -> ppsData = withStartCode(accessUnit, nal.start, nal.end)
                1, 5 -> { sliceStart = nal.start; sliceEnd = nal.end }
            }
        }

        if (!configured) {
            val sps = spsData
            val pps = ppsData
            if (sps != null && pps != null && width > 0 && height > 0) {
                configureCodec(sps, pps)
            }
        }

        if (!configured || sliceStart < 0) return

        queueSlice(accessUnit, sliceStart, sliceEnd)
    }

    @Synchronized
    fun release() {
        configured = false
        try {
            codec?.stop()
        } catch (_: Exception) {
        }
        try {
            codec?.release()
        } catch (_: Exception) {
        }
        codec = null
    }

    private fun configureCodec(sps: ByteArray, pps: ByteArray) {
        try {
            val format = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, width, height)
            format.setByteBuffer("csd-0", ByteBuffer.wrap(sps))
            format.setByteBuffer("csd-1", ByteBuffer.wrap(pps))
            // Without this, some hardware decoders hold onto 1-2 extra
            // frames internally for smoother-looking playback of recorded
            // video, which is exactly the wrong tradeoff for a live mirror —
            // it just adds frames of latency with nothing to show for it.
            if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.R) {
                format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            }

            val mc = MediaCodec.createDecoderByType(MediaFormat.MIMETYPE_VIDEO_AVC)
            mc.configure(format, surface, null, 0)
            mc.start()
            codec = mc
            configured = true
        } catch (e: Exception) {
            Log.e(TAG, "Failed to configure decoder", e)
            configured = false
        }
    }

    private fun queueSlice(accessUnit: ByteArray, sliceStart: Int, sliceEnd: Int) {
        val mc = codec ?: return
        try {
            val inputIndex = mc.dequeueInputBuffer(10_000)
            if (inputIndex < 0) return
            val inputBuffer = mc.getInputBuffer(inputIndex) ?: return
            inputBuffer.clear()
            // Re-include the NAL's start code so the decoder sees a valid
            // Annex-B buffer (sliceStart/sliceEnd bound only the NAL payload).
            inputBuffer.put(byteArrayOf(0, 0, 0, 1))
            inputBuffer.put(accessUnit, sliceStart, sliceEnd - sliceStart)
            mc.queueInputBuffer(inputIndex, 0, 4 + (sliceEnd - sliceStart), System.nanoTime() / 1000, 0)

            val info = MediaCodec.BufferInfo()
            var outputIndex = mc.dequeueOutputBuffer(info, 0)
            while (outputIndex >= 0) {
                mc.releaseOutputBuffer(outputIndex, true)
                outputIndex = mc.dequeueOutputBuffer(info, 0)
            }
        } catch (e: Exception) {
            Log.e(TAG, "Decode step failed", e)
        }
    }

    private fun withStartCode(data: ByteArray, start: Int, end: Int): ByteArray {
        val len = end - start
        val out = ByteArray(4 + len)
        out[3] = 1
        System.arraycopy(data, start, out, 4, len)
        return out
    }

    private data class NalRange(val start: Int, val end: Int)

    /** Mirrors AnnexBAccessUnitSplitter.cs / H264StreamDecoder.swift: locates NAL payload ranges (start-code bytes excluded). */
    private fun splitAnnexB(data: ByteArray): List<NalRange> {
        val markers = mutableListOf<Pair<Int, Int>>() // (start-code begin, nal payload begin)
        var i = 0
        while (i + 2 < data.size) {
            if (data[i] == 0.toByte() && data[i + 1] == 0.toByte() && data[i + 2] == 1.toByte()) {
                markers.add(i to i + 3)
                i += 3
            } else if (i + 3 < data.size && data[i] == 0.toByte() && data[i + 1] == 0.toByte() &&
                data[i + 2] == 0.toByte() && data[i + 3] == 1.toByte()
            ) {
                markers.add(i to i + 4)
                i += 4
            } else {
                i += 1
            }
        }

        val result = mutableListOf<NalRange>()
        for (idx in markers.indices) {
            val nalStart = markers[idx].second
            val nalEnd = if (idx + 1 < markers.size) markers[idx + 1].first else data.size
            if (nalStart < nalEnd) result.add(NalRange(nalStart, nalEnd))
        }
        return result
    }

    companion object {
        private const val TAG = "VideoDecoder"
    }
}
