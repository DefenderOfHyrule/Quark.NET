package io.github.quark.service

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData

data class TransferProgress(val fileName: String, val transferred: Long, val total: Long)

object TransferState {
    private val lock = Any()
    private val active = LinkedHashMap<String, TransferProgress>()
    private val _state = MutableLiveData<Map<String, TransferProgress>>(emptyMap())
    val state: LiveData<Map<String, TransferProgress>> = _state

    fun update(key: String, fileName: String, transferred: Long, total: Long) {
        synchronized(lock) { active[key] = TransferProgress(fileName, transferred, total) }
        publish()
    }

    fun clear(key: String) {
        synchronized(lock) { active.remove(key) }
        publish()
    }

    private fun publish() {
        synchronized(lock) { _state.postValue(active.toMap()) }
    }
}
