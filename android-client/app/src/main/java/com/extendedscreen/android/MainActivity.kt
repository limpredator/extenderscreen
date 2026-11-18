package com.extendedscreen.android

import android.graphics.SurfaceTexture
import android.os.Bundle
import android.util.Log
import android.view.Surface
import android.view.TextureView
import android.view.View
import android.widget.ImageButton
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import com.google.android.material.button.MaterialButton
import com.google.android.material.textfield.TextInputEditText

class MainActivity : AppCompatActivity(), StreamClient.Listener, TextureView.SurfaceTextureListener {

    private lateinit var connectCard: View
    private lateinit var hostInput: TextInputEditText
    private lateinit var connectButton: MaterialButton
    private lateinit var usbConnectButton: MaterialButton
    private lateinit var statusText: TextView
    private lateinit var disconnectButton: ImageButton
    private lateinit var videoSurface: TextureView

    private val client = StreamClient(this)
    private var decoder: VideoDecoder? = null

    // The SurfaceTexture TextureView currently has live, and the stream's
    // reported size. Either can change independently (a stream can connect
    // before the surface is ready, and Android can hand TextureView a brand
    // new SurfaceTexture after the app backgrounds/foregrounds) — so the
    // decoder is always rebuilt from scratch against whatever the CURRENT
    // values are, in resetDecoder(), rather than created once and assumed
    // to stay valid.
    private var currentSurfaceTexture: SurfaceTexture? = null
    private var currentSurface: Surface? = null
    private var pendingWidth = 0
    private var pendingHeight = 0

    private val prefs by lazy { getSharedPreferences("extendedscreen", MODE_PRIVATE) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        connectCard = findViewById(R.id.connectCard)
        hostInput = findViewById(R.id.hostInput)
        connectButton = findViewById(R.id.connectButton)
        usbConnectButton = findViewById(R.id.usbConnectButton)
        statusText = findViewById(R.id.statusText)
        disconnectButton = findViewById(R.id.disconnectButton)
        videoSurface = findViewById(R.id.videoSurface)
        videoSurface.surfaceTextureListener = this

        hostInput.setText(prefs.getString(LAST_HOST_KEY, ""))

        connectButton.setOnClickListener {
            val host = hostInput.text?.toString()?.trim().orEmpty()
            if (host.isEmpty()) return@setOnClickListener
            prefs.edit().putString(LAST_HOST_KEY, host).apply()
            statusText.visibility = View.GONE
            client.connect(host)
        }

        usbConnectButton.setOnClickListener {
            // The PC side forwards this with `adb reverse tcp:7654 tcp:7654`,
            // so "the tablet" and "the PC" are the same endpoint from the
            // tablet's point of view — no IP address needed, works over USB
            // on any network (or none) as long as the cable's plugged in and
            // USB debugging is authorized.
            statusText.visibility = View.GONE
            client.connect(USB_HOST)
        }

        disconnectButton.setOnClickListener {
            client.disconnect()
            releaseDecoder()
            showConnectCard()
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        client.disconnect()
        releaseDecoder()
    }

    private fun releaseDecoder() {
        decoder?.release()
        decoder = null
    }

    /** Always tears down whatever decoder exists and rebuilds it against the current surface + size, if both are known. */
    private fun resetDecoder() {
        releaseDecoder()
        val surfaceTexture = currentSurfaceTexture ?: return
        if (pendingWidth <= 0 || pendingHeight <= 0) return
        Log.i(TAG, "Creating decoder for ${pendingWidth}x$pendingHeight")
        surfaceTexture.setDefaultBufferSize(pendingWidth, pendingHeight)
        // Reuse one Surface per SurfaceTexture instead of wrapping a fresh
        // Surface(surfaceTexture) on every reset — a second producer
        // connecting to the same SurfaceTexture before the first one is
        // fully torn down left the buffer queue's dimensions out of sync
        // with setDefaultBufferSize, which is what produced a decoded image
        // that only filled the top few rows before falling back to a flat
        // green (uninitialized-buffer) fill for the rest of the frame.
        val surface = currentSurface ?: Surface(surfaceTexture).also { currentSurface = it }
        decoder = VideoDecoder(surface).apply { setExpectedSize(pendingWidth, pendingHeight) }
    }

    // MARK: - TextureView.SurfaceTextureListener

    override fun onSurfaceTextureAvailable(surfaceTexture: SurfaceTexture, width: Int, height: Int) {
        Log.i(TAG, "onSurfaceTextureAvailable ${width}x$height")
        currentSurfaceTexture = surfaceTexture
        resetDecoder()
    }

    override fun onSurfaceTextureSizeChanged(surfaceTexture: SurfaceTexture, width: Int, height: Int) {}

    override fun onSurfaceTextureDestroyed(surfaceTexture: SurfaceTexture): Boolean {
        Log.i(TAG, "onSurfaceTextureDestroyed")
        if (currentSurfaceTexture === surfaceTexture) {
            currentSurfaceTexture = null
            releaseDecoder()
            currentSurface?.release()
            currentSurface = null
        }
        return true // we own the Surface we created from this texture; safe to release it
    }

    override fun onSurfaceTextureUpdated(surfaceTexture: SurfaceTexture) {}

    // MARK: - StreamClient.Listener (called on the client's background thread)

    override fun onStreamInfo(width: Int, height: Int, fps: Int) {
        Log.i(TAG, "onStreamInfo ${width}x$height @ ${fps}fps")
        pendingWidth = width
        pendingHeight = height
        runOnUiThread {
            hideConnectCard()
            resetDecoder()
        }
    }

    override fun onAccessUnit(data: ByteArray) {
        decoder?.feed(data)
    }

    override fun onDisconnected(error: Throwable?) {
        Log.i(TAG, "onDisconnected error=$error")
        runOnUiThread {
            releaseDecoder()
            showConnectCard()
            if (error != null) {
                statusText.text = error.message ?: error.toString()
                statusText.visibility = View.VISIBLE
            }
        }
    }

    private fun hideConnectCard() {
        connectCard.visibility = View.GONE
        disconnectButton.visibility = View.VISIBLE
    }

    private fun showConnectCard() {
        connectCard.visibility = View.VISIBLE
        disconnectButton.visibility = View.GONE
    }

    companion object {
        private const val LAST_HOST_KEY = "last_host"
        private const val TAG = "MainActivity"
        private const val USB_HOST = "127.0.0.1"
    }
}
