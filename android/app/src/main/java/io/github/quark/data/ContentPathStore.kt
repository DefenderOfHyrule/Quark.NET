package io.github.quark.data

import android.content.Context
import android.content.SharedPreferences
import org.json.JSONArray
import org.json.JSONObject
import java.security.SecureRandom

object ContentPathStore {

    private const val PREFS_NAME = "quark_content_paths"
    private const val KEY_ENTRIES = "entries"

    private lateinit var prefs: SharedPreferences
    private val lock = Any()
    private var cache: MutableList<ContentPath> = mutableListOf()

    fun init(context: Context) {
        prefs = context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        reload()
    }

    private fun reload() {
        synchronized(lock) {
            cache.clear()
            val raw = prefs.getString(KEY_ENTRIES, null) ?: return
            try {
                val arr = JSONArray(raw)
                for (i in 0 until arr.length()) {
                    val obj = arr.getJSONObject(i)
                    cache.add(
                        ContentPath(
                            id = obj.getString("id"),
                            name = obj.getString("name"),
                            treeUri = obj.getString("treeUri"),
                        )
                    )
                }
            } catch (e: Exception) { }
        }
    }

    private fun persist() {
        synchronized(lock) {
            val arr = JSONArray()
            for (entry in cache) {
                val obj = JSONObject()
                obj.put("id", entry.id)
                obj.put("name", entry.name)
                obj.put("treeUri", entry.treeUri)
                arr.put(obj)
            }
            prefs.edit().putString(KEY_ENTRIES, arr.toString()).apply()
        }
    }

    fun all(): List<ContentPath> = synchronized(lock) { cache.toList() }

    fun count(): Int = synchronized(lock) { cache.size }

    fun get(index: Int): ContentPath? = synchronized(lock) {
        if (index in cache.indices) cache[index] else null
    }

    fun findById(id: String): ContentPath? = synchronized(lock) {
        cache.firstOrNull { it.id == id }
    }

    fun add(name: String, treeUri: String): ContentPath {
        val entry = ContentPath(id = generateId(), name = name, treeUri = treeUri)
        synchronized(lock) {
            cache.add(entry)
            persist()
        }
        return entry
    }

    fun rename(id: String, newName: String): ContentPath? {
        synchronized(lock) {
            val index = cache.indexOfFirst { it.id == id }
            if (index < 0) return null
            val updated = cache[index].copy(name = newName)
            cache[index] = updated
            persist()
            return updated
        }
    }

    fun remove(id: String) {
        synchronized(lock) {
            cache.removeAll { it.id == id }
            persist()
        }
    }

    private fun generateId(): String {
        val bytes = ByteArray(6)
        SecureRandom().nextBytes(bytes)
        return bytes.joinToString("") { "%02x".format(it) }
    }
}
