package io.github.quark.proto

interface CommandChannel {
    val remoteLabel: String
    fun readBytes(length: Int): ByteArray?
    fun writeBytes(data: ByteArray, length: Int = -1): Boolean
    fun isAlive(): Boolean
    fun close()
}
