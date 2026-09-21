package io.github.quark.proto

import android.hardware.usb.UsbDeviceConnection
import android.hardware.usb.UsbEndpoint

class UsbCommandChannel(
    private val connection: UsbDeviceConnection,
    private val endpointOut: UsbEndpoint,
    private val endpointIn: UsbEndpoint,
    override val remoteLabel: String,
) : CommandChannel {

    private val chunkSize = 16384
    @Volatile private var alive = true

    override fun readBytes(length: Int): ByteArray? {
        val buffer = ByteArray(length)
        var received = 0
        while (received < length) {
            val want = minOf(chunkSize, length - received)
            val got = connection.bulkTransfer(endpointIn, buffer, received, want, 0)
            if (got <= 0) {
                alive = false
                return null
            }
            received += got
        }
        return buffer
    }

    override fun writeBytes(data: ByteArray, length: Int): Boolean {
        val len = if (length < 0) data.size else length
        var sent = 0
        while (sent < len) {
            val want = minOf(chunkSize, len - sent)
            val wrote = connection.bulkTransfer(endpointOut, data, sent, want, 0)
            if (wrote <= 0) {
                alive = false
                return false
            }
            sent += wrote
        }
        return true
    }

    override fun isAlive(): Boolean = alive

    override fun close() {
        alive = false
        try { connection.close() } catch (e: Exception) { }
    }
}
