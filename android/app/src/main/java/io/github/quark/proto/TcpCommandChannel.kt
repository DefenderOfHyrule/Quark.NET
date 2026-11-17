package io.github.quark.proto

import java.net.Socket

class TcpCommandChannel(private val socket: Socket) : CommandChannel {

    private val input = socket.getInputStream()
    private val output = socket.getOutputStream()

    override val remoteLabel: String
        get() = socket.inetAddress?.hostAddress ?: "unknown"

    override fun readBytes(length: Int): ByteArray? {
        val buffer = ByteArray(length)
        var received = 0
        try {
            while (received < length) {
                val read = input.read(buffer, received, length - received)
                if (read <= 0) return null
                received += read
            }
            return buffer
        } catch (e: Exception) {
            return null
        }
    }

    override fun writeBytes(data: ByteArray, length: Int): Boolean {
        val len = if (length < 0) data.size else length
        return try {
            output.write(data, 0, len)
            output.flush()
            true
        } catch (e: Exception) {
            false
        }
    }

    override fun isAlive(): Boolean = socket.isConnected && !socket.isClosed

    override fun close() {
        try { input.close() } catch (e: Exception) { }
        try { output.close() } catch (e: Exception) { }
        try { socket.close() } catch (e: Exception) { }
    }
}
