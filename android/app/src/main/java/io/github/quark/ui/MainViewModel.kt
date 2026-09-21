package io.github.quark.ui

import android.content.Context
import android.content.Intent
import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import io.github.quark.QuarkApp
import io.github.quark.data.ContentPath
import io.github.quark.data.ContentPathStore
import io.github.quark.service.ConnectionState
import io.github.quark.service.LogBus
import io.github.quark.service.QuarkService
import io.github.quark.service.TransferProgress
import io.github.quark.service.TransferState
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

class MainViewModel : ViewModel() {

    private val _contentPaths = MutableLiveData<List<ContentPath>>()
    val contentPaths: LiveData<List<ContentPath>> = _contentPaths

    val connectionState: LiveData<ConnectionState.Snapshot> = ConnectionState.state
    val transferState: LiveData<Map<String, TransferProgress>> = TransferState.state
    val log: LiveData<List<String>> = LogBus.log
    val serverRunning: LiveData<Boolean> = QuarkService.isRunning

    private val prefs = QuarkApp.instance.getSharedPreferences("quark_prefs", Context.MODE_PRIVATE)

    var autoStart: Boolean
        get() = prefs.getBoolean("auto_start", false)
        set(value) { prefs.edit().putBoolean("auto_start", value).apply() }

    init {
        refreshContentPaths()
    }

    fun refreshContentPaths() {
        _contentPaths.postValue(ContentPathStore.all())
    }

    fun addContentPath(name: String, treeUri: String) {
        ContentPathStore.add(name, treeUri)
        refreshContentPaths()
    }

    fun renameContentPath(id: String, newName: String) {
        ContentPathStore.rename(id, newName)
        refreshContentPaths()
    }

    fun removeContentPath(context: Context, path: ContentPath) {
        viewModelScope.launch(Dispatchers.IO) {
            try {
                context.contentResolver.releasePersistableUriPermission(
                    android.net.Uri.parse(path.treeUri),
                    Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                )
            } catch (e: Exception) { }
            ContentPathStore.remove(path.id)
            refreshContentPaths()
        }
    }

    fun startServer(context: Context) = QuarkService.start(context)
    fun stopServer(context: Context) = QuarkService.stop(context)

    fun logMessage(line: String) = LogBus.append(line)
    fun clearLog() = LogBus.clear()
}
