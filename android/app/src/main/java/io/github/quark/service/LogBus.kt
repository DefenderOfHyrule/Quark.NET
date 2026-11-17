package io.github.quark.service

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData

object LogBus {
    private val lines = mutableListOf<String>()
    private val lock = Any()
    private val _log = MutableLiveData<List<String>>(emptyList())
    val log: LiveData<List<String>> = _log

    fun append(line: String) {
        synchronized(lock) {
            lines.add(line)
            if (lines.size > 500) lines.removeAt(0)
            _log.postValue(lines.toList())
        }
    }

    fun clear() {
        synchronized(lock) {
            lines.clear()
            _log.postValue(emptyList())
        }
    }
}
