package com.extendedscreen.android

import java.io.DataInputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Client side of ../../docs/PROTOCOL.md: connects over TCP, reads the
 * one-time handshake, then keeps delivering VIDEO_FRAME payloads (raw
 * Annex-B access units) until the connection drops. Runs its own thread;
 * all [Listener] callbacks happen on that thread, not the caller's.
 */
class StreamClient(private val listener: Listener) {

    interface Listener {
        fun onStreamInfo(width: Int, height: Int, fps: Int)
        fun onAccessUnit(data: ByteArray)
        fun onDisconnected(error: Throwable?)
    }

    private var socket: Socket? = null
    private var thread: Thread? = null
    private val running = AtomicBoolean(false)

    fun connect(host: String, port: Int = 7654) {
        disconnect()
        running.set(true)
        thread = Thread({ runLoop(host, port) }, "StreamClient").also { it.start() }
    }

    fun disconnect() {
        running.set(false)
        try {
            socket?.close()
        } catch (_: IOException) {
            // already gone
        }
        socket = null
        thread = null
    }

    private fun runLoop(host: String, port: Int) {
        try {
            val sock = Socket()
            sock.connect(InetSocketAddress(host, port), 5000)
            sock.tcpNoDelay = true
            socket = sock

            val input = DataInputStream(sock.getInputStream())

            val magic = ByteArray(4)
            input.readFully(magic)
            check(magic.contentEquals(byteArrayOf('E'.code.toByte(), 'X'.code.toByte(), 'S'.code.toByte(), 'C'.code.toByte()))) {
                "Unexpected data from server (bad handshake magic)"
            }

            input.readUnsignedByte() // protocol version, unused for now
            val width = input.readInt()
            val height = input.readInt()
            val fps = input.readInt()
            listener.onStreamInfo(width, height, fps)

            while (running.get()) {
                val type = input.readUnsignedByte()
                val length = input.readInt()
                val payload = ByteArray(length)
                input.readFully(payload)
                if (type == 0x01) {
                    listener.onAccessUnit(payload)
                }
            }
            listener.onDisconnected(null)
        } catch (t: Throwable) {
            if (running.get()) {
                listener.onDisconnected(t)
            }
        } finally {
            running.set(false)
        }
    }
}
