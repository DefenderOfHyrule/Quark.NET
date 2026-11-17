package io.github.quark.service

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData

data class UsbEntry(val key: String, val label: String, val consoleId: String)
data class NetworkEntry(val key: String, val ip: String, val consoleId: String)

object ConnectionState {

    data class Snapshot(
        val usbDevices: List<UsbEntry>,
        val networkClients: List<NetworkEntry>,
    ) {
        val usbCount get() = usbDevices.size
        val netCount get() = networkClients.size
        val isIdle get() = usbCount == 0 && netCount == 0
        val totalCount get() = usbCount + netCount
    }

    private val lock = Any()
    private val usbMap = LinkedHashMap<String, Pair<String, String>>()
    private val netMap = LinkedHashMap<String, Pair<String, String>>()

    private val _state = MutableLiveData(Snapshot(emptyList(), emptyList()))
    val state: LiveData<Snapshot> = _state

    fun addUsb(key: String, label: String, consoleId: String) {
        synchronized(lock) { usbMap[key] = Pair(label, consoleId) }
        publish()
    }

    fun removeUsb(key: String) {
        synchronized(lock) { usbMap.remove(key) }
        publish()
    }

    fun addNetwork(key: String, ip: String, consoleId: String) {
        synchronized(lock) { netMap[key] = Pair(ip, consoleId) }
        publish()
    }

    fun removeNetwork(key: String) {
        synchronized(lock) { netMap.remove(key) }
        publish()
    }

    fun renameNetwork(oldId: String, newId: String) {
        synchronized(lock) {
            val key = netMap.entries.firstOrNull { it.value.second == oldId }?.key
            if (key != null) {
                val ip = netMap[key]!!.first
                netMap[key] = Pair(ip, newId)
            }
        }
        publish()
    }

    fun renameUsb(oldId: String, newId: String) {
        synchronized(lock) {
            val key = usbMap.entries.firstOrNull { it.value.second == oldId }?.key
            if (key != null) {
                val label = usbMap[key]!!.first
                usbMap[key] = Pair(label, newId)
            }
        }
        publish()
    }

    private fun publish() {
        synchronized(lock) {
            val usbList = usbMap.entries.map { (key, value) -> UsbEntry(key, value.first, value.second) }
            val netList = netMap.entries.map { (key, value) -> NetworkEntry(key, value.first, value.second) }
            _state.postValue(Snapshot(usbList, netList))
        }
    }
}
