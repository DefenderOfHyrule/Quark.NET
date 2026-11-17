package io.github.quark.proto

import android.content.Context
import android.os.ParcelFileDescriptor
import java.io.FileInputStream
import java.io.OutputStream

interface ProgressListener {
    fun onProgress(fileName: String, transferred: Long, total: Long)
    fun onIdle()
}

class CommandSession(
    private val context: Context,
    var consoleId: String,
    var listener: ProgressListener? = null,
) {
    private var readDescriptor: ParcelFileDescriptor? = null
    private var readStream: FileInputStream? = null
    private var writeStream: OutputStream? = null

    var currentFileName: String = ""
    var currentFileSize: Long = 0
    var currentFileTransferred: Long = 0

    fun startFile(path: String, mode: Int) {
        when (mode) {
            Protocol.FILE_MODE_READ -> {
                closeRead()
                val doc = VirtualFs.resolve(context, path) ?: throw java.io.IOException("not found")
                val descriptor = context.contentResolver.openFileDescriptor(doc.uri, "r")
                    ?: throw java.io.IOException("could not open")
                readDescriptor = descriptor
                readStream = FileInputStream(descriptor.fileDescriptor)
                currentFileSize = doc.length()
            }
            Protocol.FILE_MODE_WRITE, Protocol.FILE_MODE_APPEND -> {
                closeWrite()
                val existingDoc = VirtualFs.resolve(context, path)
                currentFileSize = existingDoc?.length() ?: 0
                val uri = VirtualFs.openOrCreateForWrite(context, path)
                    ?: throw java.io.IOException("could not create")
                val mode2 = if (mode == Protocol.FILE_MODE_APPEND) "wa" else "wt"
                writeStream = context.contentResolver.openOutputStream(uri, mode2)
                    ?: throw java.io.IOException("could not open for write")
            }
        }
        currentFileName = path.substringAfterLast('/')
        currentFileTransferred = 0
    }

    fun readAt(offset: Long, size: Int): ByteArray {
        val stream = readStream
        val result = ByteArray(size)
        var readTotal = 0
        if (stream != null) {
            stream.channel.position(offset)
            while (readTotal < size) {
                val got = stream.read(result, readTotal, size - readTotal)
                if (got <= 0) break
                readTotal += got
            }
        }
        return if (readTotal == size) result else result.copyOf(readTotal)
    }

    fun readOneOff(path: String, offset: Long, size: Int): ByteArray {
        val doc = VirtualFs.resolve(context, path) ?: return ByteArray(0)
        context.contentResolver.openFileDescriptor(doc.uri, "r")?.use { descriptor ->
            FileInputStream(descriptor.fileDescriptor).use { stream ->
                stream.channel.position(offset)
                val result = ByteArray(size)
                var readTotal = 0
                while (readTotal < size) {
                    val got = stream.read(result, readTotal, size - readTotal)
                    if (got <= 0) break
                    readTotal += got
                }
                return if (readTotal == size) result else result.copyOf(readTotal)
            }
        }
        return ByteArray(0)
    }

    fun hasOpenRead(): Boolean = readStream != null

    fun writeChunk(data: ByteArray) {
        writeStream?.write(data)
    }

    fun writeOneOff(path: String, data: ByteArray) {
        val uri = VirtualFs.openOrCreateForWrite(context, path) ?: throw java.io.IOException("could not create")
        context.contentResolver.openOutputStream(uri, "wt")?.use { it.write(data) }
    }

    fun hasOpenWrite(): Boolean = writeStream != null

    fun endFile(mode: Int) {
        when (mode) {
            Protocol.FILE_MODE_READ -> closeRead()
            else -> closeWrite()
        }
        listener?.onIdle()
    }

    private fun closeRead() {
        try { readStream?.close() } catch (e: Exception) { }
        try { readDescriptor?.close() } catch (e: Exception) { }
        readStream = null
        readDescriptor = null
    }

    private fun closeWrite() {
        try { writeStream?.close() } catch (e: Exception) { }
        writeStream = null
    }

    fun dispose() {
        closeRead()
        closeWrite()
    }
}
