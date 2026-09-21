package io.github.quark.proto

import android.content.Context
import io.github.quark.data.ContentPathStore

object CommandDispatcher {

    var selectFileProvider: (() -> String?)? = null
    var onConsoleIdAnnounced: ((String, String) -> Unit)? = null
    var onUnknownCommand: ((Int) -> Unit)? = null
    var onTrace: ((String) -> Unit)? = null

    fun dispatch(context: Context, block: CommandBlock, session: CommandSession): Boolean {
        val cmdId = block.validateCommand()
        when (cmdId) {
            Protocol.CMD_GET_DRIVE_COUNT -> {
                block.responseStart()
                block.writeInt32(0)
                block.responseEnd()
            }

            Protocol.CMD_GET_DRIVE_INFO -> {
                block.readInt32()
                block.respondFailure(Protocol.RESULT_INVALID_INDEX)
            }

            Protocol.CMD_STAT_PATH -> {
                val path = block.readString()
                try {
                    val (pathType, size) = VirtualFs.stat(context, path)
                    onTrace?.invoke("StatPath \"$path\" -> type=$pathType size=$size")
                    block.responseStart()
                    block.writeInt32(pathType)
                    block.writeInt64(size)
                    block.responseEnd()
                } catch (e: Exception) {
                    block.respondFailure(Protocol.RESULT_EXCEPTION_CAUGHT)
                }
            }

            Protocol.CMD_GET_FILE_COUNT -> {
                val path = block.readString()
                val count = VirtualFs.listFileNames(context, path).size
                onTrace?.invoke("GetFileCount \"$path\" -> $count")
                block.responseStart()
                block.writeInt32(count)
                block.responseEnd()
            }

            Protocol.CMD_GET_FILE -> {
                val path = block.readString()
                val index = block.readInt32()
                val files = VirtualFs.listFileNames(context, path)
                if (index < files.size) {
                    onTrace?.invoke("GetFile \"$path\"[$index] -> \"${files[index]}\"")
                    block.responseStart()
                    block.writeString(files[index])
                    block.responseEnd()
                } else {
                    onTrace?.invoke("GetFile \"$path\"[$index] -> INVALID_INDEX (have ${files.size})")
                    block.respondFailure(Protocol.RESULT_INVALID_INDEX)
                }
            }

            Protocol.CMD_GET_DIRECTORY_COUNT -> {
                val path = block.readString()
                val count = VirtualFs.listDirectoryNames(context, path).size
                onTrace?.invoke("GetDirectoryCount \"$path\" -> $count")
                block.responseStart()
                block.writeInt32(count)
                block.responseEnd()
            }

            Protocol.CMD_GET_DIRECTORY -> {
                val path = block.readString()
                val index = block.readInt32()
                val dirs = VirtualFs.listDirectoryNames(context, path)
                if (index < dirs.size) {
                    onTrace?.invoke("GetDirectory \"$path\"[$index] -> \"${dirs[index]}\"")
                    block.responseStart()
                    block.writeString(dirs[index])
                    block.responseEnd()
                } else {
                    onTrace?.invoke("GetDirectory \"$path\"[$index] -> INVALID_INDEX (have ${dirs.size})")
                    block.respondFailure(Protocol.RESULT_INVALID_INDEX)
                }
            }

            Protocol.CMD_START_FILE -> {
                val path = block.readString()
                val mode = block.readInt32()
                onTrace?.invoke("StartFile \"$path\" mode=$mode")
                try {
                    if (mode != Protocol.FILE_MODE_READ && mode != Protocol.FILE_MODE_WRITE && mode != Protocol.FILE_MODE_APPEND) {
                        block.respondFailure(Protocol.RESULT_INVALID_FILE_MODE)
                    } else {
                        session.startFile(path, mode)
                        block.respondEmpty()
                    }
                } catch (e: Exception) {
                    onTrace?.invoke("StartFile \"$path\" threw: ${e.message}")
                    block.respondFailure(Protocol.RESULT_EXCEPTION_CAUGHT)
                }
            }

            Protocol.CMD_READ_FILE -> {
                val path = block.readString()
                val offset = block.readInt64()
                val size = block.readInt64()
                try {
                    val data = if (session.hasOpenRead()) {
                        session.readAt(offset, size.toInt())
                    } else {
                        session.readOneOff(path, offset, size.toInt())
                    }
                    session.currentFileTransferred = offset + data.size
                    if (session.currentFileSize > 0) {
                        session.listener?.onProgress(session.currentFileName, session.currentFileTransferred, session.currentFileSize)
                    }
                    block.responseStart()
                    block.writeInt64(data.size.toLong())
                    block.responseEnd()
                    block.sendBuffer(data, data.size)
                } catch (e: Exception) {
                    block.respondFailure(Protocol.RESULT_EXCEPTION_CAUGHT)
                }
            }

            Protocol.CMD_WRITE_FILE -> {
                val path = block.readString()
                val size = block.readInt64()
                val data = block.getBuffer(size.toInt())
                try {
                    if (session.hasOpenWrite()) {
                        session.writeChunk(data)
                    } else {
                        session.writeOneOff(path, data)
                    }
                    session.currentFileTransferred += size
                    if (session.currentFileSize > 0) {
                        session.listener?.onProgress(session.currentFileName, session.currentFileTransferred, session.currentFileSize)
                    }
                    block.responseStart()
                    block.writeInt64(size)
                    block.responseEnd()
                } catch (e: Exception) {
                    block.respondFailure(Protocol.RESULT_EXCEPTION_CAUGHT)
                }
            }

            Protocol.CMD_END_FILE -> {
                val mode = block.readInt32()
                try {
                    if (mode != Protocol.FILE_MODE_READ && mode != Protocol.FILE_MODE_WRITE && mode != Protocol.FILE_MODE_APPEND) {
                        block.respondFailure(Protocol.RESULT_INVALID_FILE_MODE)
                    } else {
                        session.endFile(mode)
                        block.respondEmpty()
                    }
                } catch (e: Exception) {
                    block.respondFailure(Protocol.RESULT_EXCEPTION_CAUGHT)
                }
            }

            Protocol.CMD_CREATE -> {
                val path = block.readString()
                val pathType = block.readInt32()
                try {
                    VirtualFs.createEntry(context, path, pathType)
                    block.respondEmpty()
                } catch (e: Exception) {
                    block.respondFailure(Protocol.RESULT_EXCEPTION_CAUGHT)
                }
            }

            Protocol.CMD_DELETE -> {
                val path = block.readString()
                try {
                    VirtualFs.delete(context, path)
                    block.respondEmpty()
                } catch (e: Exception) {
                    block.respondFailure(Protocol.RESULT_EXCEPTION_CAUGHT)
                }
            }

            Protocol.CMD_RENAME -> {
                val path = block.readString()
                val newName = block.readString()
                try {
                    VirtualFs.rename(context, path, newName)
                    block.respondEmpty()
                } catch (e: Exception) {
                    block.respondFailure(Protocol.RESULT_EXCEPTION_CAUGHT)
                }
            }

            Protocol.CMD_GET_SPECIAL_PATH_COUNT -> {
                block.responseStart()
                block.writeInt32(ContentPathStore.count())
                block.responseEnd()
            }

            Protocol.CMD_GET_SPECIAL_PATH -> {
                val index = block.readInt32()
                val entry = ContentPathStore.get(index)
                if (entry != null) {
                    onTrace?.invoke("GetSpecialPath $index -> name=\"${entry.name}\" path=\"/${entry.id}\"")
                    block.responseStart()
                    block.writeString(entry.name)
                    block.writeString("/${entry.id}")
                    block.responseEnd()
                } else {
                    block.respondFailure(Protocol.RESULT_INVALID_INDEX)
                }
            }

            Protocol.CMD_SELECT_FILE -> {
                val picked = selectFileProvider?.invoke()
                if (picked != null) {
                    block.responseStart()
                    block.writeString(picked)
                    block.responseEnd()
                } else {
                    block.respondFailure(Protocol.RESULT_SELECTION_CANCELLED)
                }
            }

            Protocol.CMD_ANNOUNCE_CONSOLE_ID -> {
                val announced = block.readString()
                if (announced.isNotEmpty()) {
                    val old = session.consoleId
                    session.consoleId = announced
                    onConsoleIdAnnounced?.invoke(old, announced)
                }
                block.respondEmpty()
            }

            else -> {
                onUnknownCommand?.invoke(cmdId)
                return false
            }
        }
        return true
    }
}
