package io.github.quark.proto

import java.nio.ByteBuffer
import java.nio.ByteOrder

class CommandBlock(private val channel: CommandChannel) {

    private val incoming: ByteArray?
    private var inPos = 0
    private val outgoing = ByteArray(Protocol.BLOCK_SIZE)
    private var outPos = 0

    init {
        incoming = channel.readBytes(Protocol.BLOCK_SIZE)
    }

    fun isValid(): Boolean = incoming != null

    fun validateCommand(): Int {
        val magic = readInt32Unsigned()
        return if (magic == Protocol.INPUT_MAGIC) readInt32() else Protocol.INVALID_CMD_ID
    }

    fun readInt32(): Int = readInt32Unsigned()

    fun readInt64(): Long {
        val buf = incoming!!
        val value = ByteBuffer.wrap(buf, inPos, 8).order(ByteOrder.LITTLE_ENDIAN).long
        inPos += 8
        return value
    }

    fun readString(): String {
        val len = readInt32()
        val bytes = incoming!!.copyOfRange(inPos, inPos + len)
        inPos += len
        return String(bytes, Charsets.UTF_8)
    }

    private fun readInt32Unsigned(): Int {
        val buf = incoming!!
        val value = ByteBuffer.wrap(buf, inPos, 4).order(ByteOrder.LITTLE_ENDIAN).int
        inPos += 4
        return value
    }

    fun writeInt32(value: Int) {
        ByteBuffer.wrap(outgoing, outPos, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(value)
        outPos += 4
    }

    fun writeInt64(value: Long) {
        ByteBuffer.wrap(outgoing, outPos, 8).order(ByteOrder.LITTLE_ENDIAN).putLong(value)
        outPos += 8
    }

    fun writeString(value: String) {
        val raw = value.toByteArray(Charsets.UTF_8)
        writeInt32(raw.size)
        System.arraycopy(raw, 0, outgoing, outPos, raw.size)
        outPos += raw.size
    }

    fun sendBuffer(buffer: ByteArray, length: Int) {
        channel.writeBytes(buffer, length)
    }

    fun getBuffer(length: Int): ByteArray = channel.readBytes(length) ?: ByteArray(0)

    fun responseStart() {
        outPos = 0
        ByteBuffer.wrap(outgoing, 0, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(Protocol.OUTPUT_MAGIC)
        ByteBuffer.wrap(outgoing, 4, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(Protocol.RESULT_SUCCESS)
        outPos = 8
    }

    fun responseEnd() {
        channel.writeBytes(outgoing)
    }

    fun respondFailure(code: Int) {
        outPos = 0
        ByteBuffer.wrap(outgoing, 0, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(Protocol.OUTPUT_MAGIC)
        ByteBuffer.wrap(outgoing, 4, 4).order(ByteOrder.LITTLE_ENDIAN).putInt(code)
        outPos = 8
        responseEnd()
    }

    fun respondEmpty() {
        responseStart()
        responseEnd()
    }
}
