package io.github.quark.ui

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.text.InputType
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ImageButton
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.app.AppCompatDelegate
import androidx.core.app.ActivityCompat
import androidx.activity.result.contract.ActivityResultContracts
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.google.android.material.materialswitch.MaterialSwitch
import com.google.android.material.tabs.TabLayout
import io.github.quark.QuarkApp
import io.github.quark.R
import io.github.quark.data.ContentPath
import io.github.quark.service.ConnectionState
import io.github.quark.service.FilePickerBridge
import io.github.quark.service.NetworkInfo
import io.github.quark.service.QuarkService
import io.github.quark.service.TransferProgress
import io.github.quark.update.UpdateChecker
import io.github.quark.update.UpdateInfo
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

class MainActivity : AppCompatActivity() {

    private lateinit var vm: MainViewModel
    private lateinit var adapter: ContentPathAdapter

    private lateinit var pillStatus: TextView
    private lateinit var pillDot: View
    private lateinit var networkIpLabel: TextView
    private lateinit var logView: TextView
    private lateinit var serverSwitch: MaterialSwitch
    private lateinit var autostartSwitch: MaterialSwitch

    private lateinit var btnThemeToggle: ImageButton
    private lateinit var btnLogToggle: LinearLayout
    private lateinit var logSection: LinearLayout

    private lateinit var contentPathsTabContent: LinearLayout
    private lateinit var connectionsTabContent: LinearLayout
    private lateinit var connectionsList: LinearLayout
    private lateinit var noConnectionsLabel: TextView
    private lateinit var noPathsLabel: TextView

    private var logVisible = false
    private var pendingTreeUri: Uri? = null
    private var latestSnapshot: ConnectionState.Snapshot = ConnectionState.Snapshot(emptyList(), emptyList())
    private var latestTransfers: Map<String, TransferProgress> = emptyMap()

    private val ipRefreshHandler = Handler(Looper.getMainLooper())
    private val ipRefreshRunnable = object : Runnable {
        override fun run() {
            refreshNetworkIp()
            ipRefreshHandler.postDelayed(this, 4000)
        }
    }

    private val addFolderLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK) {
            result.data?.data?.let { uri -> onFolderPicked(uri) }
        }
    }

    private val selectFileLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        val uri = if (result.resultCode == Activity.RESULT_OK) result.data?.data else null
        FilePickerBridge.deliverResult(uri)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        vm = ViewModelProvider(this)[MainViewModel::class.java]
        setContentView(R.layout.activity_main)

        pillStatus = findViewById(R.id.pill_status)
        pillDot = findViewById(R.id.pill_dot)
        networkIpLabel = findViewById(R.id.network_ip_label)
        logView = findViewById(R.id.log_view)
        serverSwitch = findViewById(R.id.switch_server)
        autostartSwitch = findViewById(R.id.switch_autostart)

        btnThemeToggle = findViewById(R.id.btn_theme_toggle)
        btnLogToggle = findViewById(R.id.btn_log_toggle)
        logSection = findViewById(R.id.log_section)

        contentPathsTabContent = findViewById(R.id.content_paths_tab_content)
        connectionsTabContent = findViewById(R.id.connections_tab_content)
        connectionsList = findViewById(R.id.connections_list)
        noConnectionsLabel = findViewById(R.id.no_connections_label)
        noPathsLabel = findViewById(R.id.no_paths_label)

        autostartSwitch.isChecked = vm.autoStart
        autostartSwitch.setOnCheckedChangeListener { _, checked -> vm.autoStart = checked }

        findViewById<TabLayout>(R.id.tab_layout).addOnTabSelectedListener(
            object : TabLayout.OnTabSelectedListener {
                override fun onTabSelected(tab: TabLayout.Tab) {
                    val showConnections = tab.position == 1
                    contentPathsTabContent.visibility = if (showConnections) View.GONE else View.VISIBLE
                    connectionsTabContent.visibility = if (showConnections) View.VISIBLE else View.GONE
                }
                override fun onTabUnselected(tab: TabLayout.Tab) {}
                override fun onTabReselected(tab: TabLayout.Tab) {}
            }
        )

        val recycler: RecyclerView = findViewById(R.id.content_path_list)
        adapter = ContentPathAdapter(
            onRename = { path -> showRenameDialog(path) },
            onDelete = { path ->
                AlertDialog.Builder(this)
                    .setTitle("Delete content path")
                    .setMessage("Remove \"${path.name}\" from content paths?")
                    .setPositiveButton("Delete") { _, _ -> vm.removeContentPath(this, path) }
                    .setNegativeButton("Cancel", null)
                    .show()
            }
        )
        recycler.layoutManager = LinearLayoutManager(this)
        recycler.adapter = adapter

        findViewById<Button>(R.id.btn_add_path).setOnClickListener {
            addFolderLauncher.launch(Intent(Intent.ACTION_OPEN_DOCUMENT_TREE))
        }

        serverSwitch.setOnCheckedChangeListener { button, checked ->
            if (!button.isPressed) return@setOnCheckedChangeListener
            if (checked) vm.startServer(this) else vm.stopServer(this)
        }

        btnThemeToggle.setOnClickListener {
            val prefs = getSharedPreferences(QuarkApp.PREFS_NAME, Context.MODE_PRIVATE)
            val isDark = prefs.getBoolean(QuarkApp.KEY_DARK_MODE, true)
            prefs.edit().putBoolean(QuarkApp.KEY_DARK_MODE, !isDark).apply()
            AppCompatDelegate.setDefaultNightMode(
                if (isDark) AppCompatDelegate.MODE_NIGHT_NO
                else AppCompatDelegate.MODE_NIGHT_YES
            )
        }

        btnLogToggle.setOnClickListener {
            logVisible = !logVisible
            logSection.visibility = if (logVisible) View.VISIBLE else View.GONE
            if (logVisible) {
                val scrollView = findViewById<androidx.core.widget.NestedScrollView>(R.id.scroll_view)
                scrollView.post { scrollView.fullScroll(View.FOCUS_DOWN) }
            }
        }

        findViewById<Button>(R.id.btn_clear_log).setOnClickListener { vm.clearLog() }
        findViewById<Button>(R.id.btn_credits).setOnClickListener { showCredits() }

        vm.contentPaths.observe(this) { list ->
            adapter.submit(list)
            noPathsLabel.visibility = if (list.isEmpty()) View.VISIBLE else View.GONE
        }

        vm.connectionState.observe(this) { snapshot ->
            latestSnapshot = snapshot
            renderConnectionState()
        }

        vm.transferState.observe(this) { transfers ->
            latestTransfers = transfers
            renderConnectionState()
        }

        vm.log.observe(this) { lines ->
            logView.text = lines.joinToString("\n")
            if (logVisible) {
                val scrollView = findViewById<androidx.core.widget.NestedScrollView>(R.id.scroll_view)
                scrollView.post { scrollView.fullScroll(View.FOCUS_DOWN) }
            }
        }

        vm.serverRunning.observe(this) { running ->
            serverSwitch.setOnCheckedChangeListener(null)
            serverSwitch.isChecked = running
            serverSwitch.setOnCheckedChangeListener { button, checked ->
                if (!button.isPressed) return@setOnCheckedChangeListener
                if (checked) vm.startServer(this) else vm.stopServer(this)
            }
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            ActivityCompat.requestPermissions(this, arrayOf(android.Manifest.permission.POST_NOTIFICATIONS), 1)
        }

        if (vm.autoStart) vm.startServer(this)
    }

    override fun onResume() {
        super.onResume()
        FilePickerBridge.requestPicker = {
            selectFileLauncher.launch(Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
                type = "*/*"
                addCategory(Intent.CATEGORY_OPENABLE)
            })
        }
        ipRefreshHandler.post(ipRefreshRunnable)
    }

    override fun onPause() {
        super.onPause()
        FilePickerBridge.requestPicker = null
        ipRefreshHandler.removeCallbacks(ipRefreshRunnable)
    }

    private fun refreshNetworkIp() {
        lifecycleScope.launch {
            val ip = withContext(Dispatchers.IO) { NetworkInfo.resolveLocalIp() }
            networkIpLabel.text = if (ip != null) {
                getString(R.string.network_ip_known, ip)
            } else {
                getString(R.string.network_ip_unknown)
            }
        }
    }

    private fun renderConnectionState() {
        val snapshot = latestSnapshot
        when {
            snapshot.isIdle -> {
                pillDot.setBackgroundResource(R.drawable.dot_idle)
                pillStatus.text = getString(R.string.status_idle)
            }
            snapshot.usbCount > 0 && snapshot.netCount == 0 -> {
                pillDot.setBackgroundResource(R.drawable.dot_usb)
                pillStatus.text = if (snapshot.usbCount == 1) {
                    getString(R.string.status_usb_single, snapshot.usbDevices[0].consoleId)
                } else {
                    getString(R.string.status_usb_multi, snapshot.usbCount)
                }
            }
            snapshot.usbCount == 0 && snapshot.netCount > 0 -> {
                pillDot.setBackgroundResource(R.drawable.dot_network)
                pillStatus.text = if (snapshot.netCount == 1) {
                    getString(R.string.status_network_single, snapshot.networkClients[0].consoleId)
                } else {
                    getString(R.string.status_network_multi, snapshot.netCount)
                }
            }
            else -> {
                pillDot.setBackgroundResource(R.drawable.dot_multi)
                pillStatus.text = "USB (${snapshot.usbCount})  Network (${snapshot.netCount})"
            }
        }

        connectionsList.removeAllViews()
        val totalRows = snapshot.usbCount + snapshot.netCount
        noConnectionsLabel.visibility = if (totalRows == 0) View.VISIBLE else View.GONE

        for (entry in snapshot.usbDevices) {
            addConnectionRow(
                key = entry.key,
                label = "USB  \u2022  ${entry.consoleId}  \u2013  ${entry.label}",
                dotRes = R.drawable.dot_usb,
            )
        }
        for (entry in snapshot.networkClients) {
            addConnectionRow(
                key = entry.key,
                label = "Network  \u2022  ${entry.consoleId}  \u2013  ${entry.ip}",
                dotRes = R.drawable.dot_network,
            )
        }
    }

    private fun addConnectionRow(key: String, label: String, dotRes: Int) {
        val view = layoutInflater.inflate(R.layout.item_connection, connectionsList, false)
        view.findViewById<View>(R.id.connection_dot).setBackgroundResource(dotRes)
        view.findViewById<TextView>(R.id.connection_label).text = label

        val progressGroup = view.findViewById<LinearLayout>(R.id.connection_progress_group)
        val transfer = latestTransfers[key]
        if (transfer != null && transfer.total > 0) {
            progressGroup.visibility = View.VISIBLE
            val pct = ((transfer.transferred.toDouble() / transfer.total.toDouble()) * 100.0)
                .coerceIn(0.0, 100.0)
            view.findViewById<TextView>(R.id.connection_file_name).text = transfer.fileName
            view.findViewById<TextView>(R.id.connection_percent).text = String.format("%.0f%%", pct)
            view.findViewById<android.widget.ProgressBar>(R.id.connection_progress_bar).progress =
                (pct * 10).toInt().coerceIn(0, 1000)
        } else {
            progressGroup.visibility = View.GONE
        }

        connectionsList.addView(view)
    }

    private fun onFolderPicked(uri: Uri) {
        pendingTreeUri = uri
        lifecycleScope.launch {
            val name = withContext(Dispatchers.IO) {
                try {
                    contentResolver.takePersistableUriPermission(
                        uri,
                        Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
                    )
                } catch (e: Exception) { }
                suggestedName(uri)
            }
            showNameDialog(name)
        }
    }

    private fun suggestedName(uri: Uri): String {
        val doc = androidx.documentfile.provider.DocumentFile.fromTreeUri(this, uri)
        return doc?.name ?: "Folder"
    }

    private fun showNameDialog(suggested: String) {
        val view = layoutInflater.inflate(R.layout.dialog_name_path, null)
        val input = view.findViewById<EditText>(R.id.path_name_input)
        input.setText(suggested)
        input.setSelection(input.text.length)

        AlertDialog.Builder(this)
            .setTitle(R.string.dialog_name_path_title)
            .setView(view)
            .setPositiveButton(R.string.dialog_name_path_confirm) { _, _ ->
                val uri = pendingTreeUri
                val name = input.text?.toString()?.trim().orEmpty().ifEmpty { suggested }
                if (uri != null) {
                    vm.addContentPath(name, uri.toString())
                    vm.logMessage("Added content path: $name")
                }
                pendingTreeUri = null
            }
            .setNegativeButton("Cancel") { _, _ -> pendingTreeUri = null }
            .show()
    }

    private fun showRenameDialog(path: ContentPath) {
        val input = EditText(this).apply {
            inputType = InputType.TYPE_CLASS_TEXT
            setSingleLine(true)
            setText(path.name)
            setSelection(text.length)
        }
        val pad = (16 * resources.displayMetrics.density).toInt()
        val container = FrameLayout(this).apply {
            setPadding(pad, pad / 2, pad, 0)
            addView(input)
        }
        AlertDialog.Builder(this)
            .setTitle(R.string.dialog_rename_path_title)
            .setView(container)
            .setPositiveButton(R.string.dialog_rename_path_confirm) { _, _ ->
                val newName = input.text?.toString()?.trim().orEmpty()
                if (newName.isNotEmpty()) vm.renameContentPath(path.id, newName)
            }
            .setNegativeButton("Cancel", null)
            .show()
    }

    private fun showCredits() {
        val dialog = BottomSheetDialog(this)
        val view = layoutInflater.inflate(R.layout.bottom_sheet_credits, null)
        view.findViewById<TextView>(R.id.credits_link).setOnClickListener {
            startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("https://github.com/DefenderOfHyrule")))
        }
        view.findViewById<Button>(R.id.btn_check_updates).setOnClickListener { checkForUpdates() }
        dialog.setContentView(view)
        dialog.show()
    }

    private fun checkForUpdates() {
        Toast.makeText(this, R.string.update_checking, Toast.LENGTH_SHORT).show()
        lifecycleScope.launch {
            var failed = false
            val info = try {
                withContext(Dispatchers.IO) { UpdateChecker.check() }
            } catch (e: Exception) {
                failed = true
                null
            }
            if (failed) {
                Toast.makeText(this@MainActivity, R.string.update_error, Toast.LENGTH_SHORT).show()
                return@launch
            }
            if (info == null) {
                Toast.makeText(this@MainActivity, R.string.update_none, Toast.LENGTH_SHORT).show()
                return@launch
            }
            confirmDownload(info)
        }
    }

    private fun confirmDownload(info: UpdateInfo) {
        AlertDialog.Builder(this)
            .setTitle(R.string.update_available_title)
            .setMessage(getString(
                R.string.update_available_message,
                info.latestVersion.toString(),
                UpdateChecker.currentVersion().toString()
            ))
            .setPositiveButton(R.string.update_download) { _, _ -> downloadAndInstall(info) }
            .setNegativeButton(R.string.update_cancel, null)
            .show()
    }

    private fun downloadAndInstall(info: UpdateInfo) {
        vm.logMessage("Downloading update ${info.assetName}...")
        lifecycleScope.launch {
            val apkFile = try {
                withContext(Dispatchers.IO) {
                    UpdateChecker.downloadApk(info) { pct ->
                        if (pct % 10 == 0) vm.logMessage("  $pct%")
                    }
                }
            } catch (e: Exception) {
                vm.logMessage("[ERROR] Update download failed: ${e.message}")
                Toast.makeText(this@MainActivity, R.string.update_error, Toast.LENGTH_SHORT).show()
                return@launch
            }
            vm.logMessage("Update downloaded, launching installer...")
            promptInstall(apkFile)
        }
    }

    private fun promptInstall(apkFile: File) {
        if (!UpdateChecker.canRequestInstallPackages(this)) {
            AlertDialog.Builder(this)
                .setTitle(R.string.update_install_permission_title)
                .setMessage(R.string.update_install_permission_message)
                .setPositiveButton(R.string.update_install_permission_settings) { _, _ ->
                    val intent = Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES)
                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                        intent.data = Uri.parse("package:$packageName")
                    }
                    startActivity(intent)
                }
                .setNegativeButton(R.string.update_cancel, null)
                .show()
            return
        }
        startActivity(UpdateChecker.installIntent(this, apkFile))
    }
}
