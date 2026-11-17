package io.github.quark.service

import android.net.Uri
import android.os.Handler
import android.os.Looper
import java.util.concurrent.SynchronousQueue
import java.util.concurrent.TimeUnit

object FilePickerBridge {
    @Volatile var requestPicker: (() -> Unit)? = null
    private val resultQueue = SynchronousQueue<Uri>()
    private val cancelledMarker: Uri = Uri.EMPTY

    fun requestSelection(timeoutMs: Long = 120_000): Uri? {
        val launcher = requestPicker ?: return null
        Handler(Looper.getMainLooper()).post { launcher() }
        val result = try {
            resultQueue.poll(timeoutMs, TimeUnit.MILLISECONDS)
        } catch (e: InterruptedException) {
            null
        }
        return if (result == null || result == cancelledMarker) null else result
    }

    fun deliverResult(uri: Uri?) {
        resultQueue.offer(uri ?: cancelledMarker)
    }
}
