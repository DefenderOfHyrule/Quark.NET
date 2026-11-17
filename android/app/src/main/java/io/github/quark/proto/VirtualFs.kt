package io.github.quark.proto

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import androidx.documentfile.provider.DocumentFile
import io.github.quark.data.ContentPathStore
import java.security.SecureRandom
import java.util.concurrent.ConcurrentHashMap

object VirtualFs {

    private const val AD_HOC_PREFIX = "ah_"
    private val adHocEntries = ConcurrentHashMap<String, DocumentFile>()

    private fun splitPath(path: String): Pair<String, List<String>> {
        val normalized = path.replace(':', '/')
        val trimmed = normalized.trim('/')
        if (trimmed.isEmpty()) return Pair("", emptyList())
        val parts = trimmed.split('/').filter { it.isNotEmpty() }
        return Pair(parts.first(), parts.drop(1))
    }

    private fun rootDocument(context: Context, rootId: String): DocumentFile? {
        if (rootId.startsWith(AD_HOC_PREFIX)) return adHocEntries[rootId]
        val contentPath = ContentPathStore.findById(rootId) ?: return null
        return DocumentFile.fromTreeUri(context, Uri.parse(contentPath.treeUri))
    }

    fun resolve(context: Context, path: String): DocumentFile? {
        val (rootId, segments) = splitPath(path)
        if (rootId.isEmpty()) return null
        var current = rootDocument(context, rootId) ?: return null
        if (rootId.startsWith(AD_HOC_PREFIX)) return current
        for (segment in segments) {
            current = current.findFile(segment) ?: return null
        }
        return current
    }

    fun resolveParentAndName(context: Context, path: String): Pair<DocumentFile, String>? {
        val (rootId, segments) = splitPath(path)
        if (rootId.isEmpty() || segments.isEmpty() || rootId.startsWith(AD_HOC_PREFIX)) return null
        var current = rootDocument(context, rootId) ?: return null
        for (segment in segments.dropLast(1)) {
            current = current.findFile(segment) ?: return null
        }
        return Pair(current, segments.last())
    }

    fun stat(context: Context, path: String): Pair<Int, Long> {
        val doc = resolve(context, path) ?: return Pair(Protocol.PATH_TYPE_INVALID, 0L)
        return if (doc.isDirectory) Pair(Protocol.PATH_TYPE_DIRECTORY, 0L)
        else Pair(Protocol.PATH_TYPE_FILE, doc.length())
    }

    private data class ChildEntry(val name: String, val isDirectory: Boolean)
    private data class CachedListing(val children: List<ChildEntry>, val timestamp: Long)

    private val listingCache = ConcurrentHashMap<String, CachedListing>()
    private const val CACHE_TTL_MS = 5000L

    private fun queryChildren(context: Context, dir: DocumentFile): List<ChildEntry> {
        val key = dir.uri.toString()
        val now = System.currentTimeMillis()
        val cached = listingCache[key]
        if (cached != null && now - cached.timestamp < CACHE_TTL_MS) {
            return cached.children
        }
        val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(
            dir.uri, DocumentsContract.getDocumentId(dir.uri)
        )
        val results = mutableListOf<ChildEntry>()
        try {
            context.contentResolver.query(
                childrenUri,
                arrayOf(DocumentsContract.Document.COLUMN_DISPLAY_NAME, DocumentsContract.Document.COLUMN_MIME_TYPE),
                null, null, null
            )?.use { cursor ->
                val nameIndex = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_DISPLAY_NAME)
                val mimeIndex = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_MIME_TYPE)
                while (cursor.moveToNext()) {
                    val name = if (nameIndex >= 0) cursor.getString(nameIndex) else null
                    if (name.isNullOrEmpty()) continue
                    val mime = if (mimeIndex >= 0) cursor.getString(mimeIndex) else null
                    val isDir = mime == DocumentsContract.Document.MIME_TYPE_DIR
                    results.add(ChildEntry(name, isDir))
                }
            }
        } catch (e: Exception) { }
        val sorted = results.sortedWith(compareBy({ !it.isDirectory }, { it.name.lowercase() }))
        listingCache[key] = CachedListing(sorted, now)
        return sorted
    }

    private fun invalidateCache() {
        listingCache.clear()
    }

    fun listFileNames(context: Context, path: String): List<String> {
        val doc = resolve(context, path) ?: return emptyList()
        if (!doc.isDirectory) return emptyList()
        return queryChildren(context, doc).filter { !it.isDirectory }.map { it.name }
    }

    fun listDirectoryNames(context: Context, path: String): List<String> {
        val doc = resolve(context, path) ?: return emptyList()
        if (!doc.isDirectory) return emptyList()
        return queryChildren(context, doc).filter { it.isDirectory }.map { it.name }
    }

    fun createEntry(context: Context, path: String, pathType: Int): Boolean {
        val existing = resolve(context, path)
        if (existing != null) return true
        val (parent, name) = resolveParentAndName(context, path) ?: return false
        val created = if (pathType == Protocol.PATH_TYPE_FILE) {
            parent.createFile("application/octet-stream", name) != null
        } else {
            parent.createDirectory(name) != null
        }
        if (created) invalidateCache()
        return created
    }

    fun delete(context: Context, path: String): Boolean {
        val doc = resolve(context, path) ?: return false
        val deleted = doc.delete()
        if (deleted) invalidateCache()
        return deleted
    }

    fun rename(context: Context, path: String, newName: String): Boolean {
        val doc = resolve(context, path) ?: return false
        val renamed = doc.renameTo(newName)
        if (renamed) invalidateCache()
        return renamed
    }

    fun openOrCreateForWrite(context: Context, path: String): Uri? {
        val existing = resolve(context, path)
        if (existing != null) return existing.uri
        val (parent, name) = resolveParentAndName(context, path) ?: return null
        val created = parent.createFile("application/octet-stream", name) ?: return null
        invalidateCache()
        return created.uri
    }

    fun registerAdHoc(doc: DocumentFile): String {
        val token = ByteArray(8).also { SecureRandom().nextBytes(it) }
            .joinToString("") { "%02x".format(it) }
        val id = "$AD_HOC_PREFIX$token"
        adHocEntries[id] = doc
        return "/$id"
    }
}

