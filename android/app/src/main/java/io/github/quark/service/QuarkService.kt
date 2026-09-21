package io.github.quark.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.usb.UsbConstants
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbDeviceConnection
import android.hardware.usb.UsbEndpoint
import android.hardware.usb.UsbManager
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import androidx.core.app.NotificationCompat
import androidx.documentfile.provider.DocumentFile
import androidx.lifecycle.MutableLiveData
import io.github.quark.R
import io.github.quark.proto.CommandBlock
import io.github.quark.proto.CommandDispatcher
import io.github.quark.proto.CommandSession
import io.github.quark.proto.Protocol
import io.github.quark.proto.ProgressListener
import io.github.quark.proto.TcpCommandChannel
import io.github.quark.proto.UsbCommandChannel
import io.github.quark.proto.VirtualFs
import io.github.quark.ui.MainActivity
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean

class QuarkService : Service() {

    private val executor = Executors.newCachedThreadPool()
    private val running = AtomicBoolean(false)
    @Volatile private var serverSocket: ServerSocket? = null
    private val openUsbDeviceKeys = java.util.concurrent.ConcurrentHashMap.newKeySet<String>()
    private val pollHandler = Handler(Looper.getMainLooper())

    private val pollRunnable = object : Runnable {
        override fun run() {
            scanUsbDevices()
            pollHandler.postDelayed(this, 2000)
        }
    }

    private val usbPermissionReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action != ACTION_USB_PERMISSION) return
            val device: UsbDevice? = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE)
            val granted = intent.getBooleanExtra(UsbManager.EXTRA_PERMISSION_GRANTED, false)
            if (device != null && granted) {
                openUsbDevice(device)
            }
        }
    }

    private val activeUsbChannels = java.util.concurrent.ConcurrentHashMap<String, UsbCommandChannel>()

    private val usbDetachReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action != UsbManager.ACTION_USB_DEVICE_DETACHED) return
            val device: UsbDevice? = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE)
            if (device != null) {
                val key = deviceKey(device)
                activeUsbChannels[key]?.close()
            }
            LogBus.append("USB device physically removed")
        }
    }

    override fun onCreate() {
        super.onCreate()
        CommandDispatcher.onConsoleIdAnnounced = { old, updated -> ConnectionState.renameNetwork(old, updated); ConnectionState.renameUsb(old, updated) }
        CommandDispatcher.selectFileProvider = {
            val uri = FilePickerBridge.requestSelection()
            if (uri != null) {
                val doc = DocumentFile.fromSingleUri(applicationContext, uri)
                if (doc != null) VirtualFs.registerAdHoc(doc) else null
            } else null
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(usbPermissionReceiver, IntentFilter(ACTION_USB_PERMISSION), Context.RECEIVER_NOT_EXPORTED)
            registerReceiver(usbDetachReceiver, IntentFilter(UsbManager.ACTION_USB_DEVICE_DETACHED), Context.RECEIVER_NOT_EXPORTED)
        } else {
            registerReceiver(usbPermissionReceiver, IntentFilter(ACTION_USB_PERMISSION))
            registerReceiver(usbDetachReceiver, IntentFilter(UsbManager.ACTION_USB_DEVICE_DETACHED))
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                stopEverything()
                return START_NOT_STICKY
            }
            ACTION_USB_DEVICE_READY -> {
                startForegroundIfNeeded()
                val device: UsbDevice? = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE)
                if (device != null) openUsbDevice(device)
            }
            else -> {
                startForegroundIfNeeded()
                startTcpServer()
                pollHandler.post(pollRunnable)
            }
        }
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        stopEverything()
        try { unregisterReceiver(usbPermissionReceiver) } catch (e: Exception) { }
        try { unregisterReceiver(usbDetachReceiver) } catch (e: Exception) { }
        super.onDestroy()
    }

    private fun startForegroundIfNeeded() {
        if (running.getAndSet(true)) return
        val channelId = "quark_service"
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = getSystemService(NotificationManager::class.java)
            val channel = NotificationChannel(channelId, "Quark server", NotificationManager.IMPORTANCE_LOW)
            manager.createNotificationChannel(channel)
        }
        val openIntent = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        val notification: Notification = NotificationCompat.Builder(this, channelId)
            .setContentTitle(getString(R.string.app_name))
            .setContentText(getString(R.string.status_idle))
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentIntent(openIntent)
            .setOngoing(true)
            .build()
        startForeground(NOTIFICATION_ID, notification)
        isRunning.postValue(true)
    }

    private fun stopEverything() {
        try { serverSocket?.close() } catch (e: Exception) { }
        serverSocket = null
        for (channel in activeUsbChannels.values) {
            try { channel.close() } catch (e: Exception) { }
        }
        pollHandler.removeCallbacks(pollRunnable)
        running.set(false)
        isRunning.postValue(false)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) stopForeground(STOP_FOREGROUND_REMOVE)
        else @Suppress("DEPRECATION") stopForeground(true)
        stopSelf()
    }

    private val serverStarting = AtomicBoolean(false)

    private fun startTcpServer() {
        if (serverSocket != null) return
        if (!serverStarting.compareAndSet(false, true)) return
        executor.execute {
            var socket: ServerSocket? = null
            var attempt = 0
            while (socket == null && attempt < 5 && running.get()) {
                try {
                    val candidate = ServerSocket()
                    candidate.reuseAddress = true
                    candidate.bind(java.net.InetSocketAddress(Protocol.TCP_PORT))
                    socket = candidate
                } catch (e: java.net.BindException) {
                    attempt++
                    LogBus.append("[WARN] Port ${Protocol.TCP_PORT} still in use, retrying ($attempt/5)...")
                    Thread.sleep(1000)
                } catch (e: Exception) {
                    LogBus.append("[ERROR] TCP server failed: ${e.message}")
                    break
                }
            }
            serverStarting.set(false)
            if (socket == null) {
                LogBus.append("[ERROR] Could not start TCP server on port ${Protocol.TCP_PORT}")
                return@execute
            }
            if (!running.get()) {
                try { socket.close() } catch (e: Exception) { }
                return@execute
            }
            serverSocket = socket
            LogBus.append("Listening on TCP port ${Protocol.TCP_PORT}")
            while (!socket.isClosed) {
                val client = try { socket.accept() } catch (e: Exception) { break }
                executor.execute { handleTcpClient(client) }
            }
        }
    }

    private fun handleTcpClient(socket: Socket) {
        val remote = socket.inetAddress?.hostAddress ?: "unknown"
        val stateKey = "net:$remote"
        val consoleId = ConsoleId.generate()
        LogBus.append("Switch connected from $remote")
        ConnectionState.addNetwork(stateKey, remote, consoleId)
        val channel = TcpCommandChannel(socket)
        val session = CommandSession(applicationContext, consoleId, transferListener(stateKey))
        try {
            while (channel.isAlive()) {
                val block = CommandBlock(channel)
                if (!block.isValid()) break
                if (!CommandDispatcher.dispatch(applicationContext, block, session)) break
            }
        } catch (e: Exception) {
            LogBus.append("[ERROR] TCP session error from $remote: ${e.message}")
        } finally {
            session.dispose()
            channel.close()
            ConnectionState.removeNetwork(stateKey)
            TransferState.clear(stateKey)
            LogBus.append("Switch disconnected: $remote")
        }
    }

    private fun scanUsbDevices() {
        val usbManager = getSystemService(Context.USB_SERVICE) as UsbManager
        for (device in usbManager.deviceList.values) {
            if (device.vendorId != Protocol.USB_VENDOR_ID || device.productId != Protocol.USB_PRODUCT_ID) continue
            val key = deviceKey(device)
            if (key in openUsbDeviceKeys) continue
            if (usbManager.hasPermission(device)) {
                openUsbDevice(device)
            } else {
                val flags = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0
                val permissionIntent = PendingIntent.getBroadcast(
                    this, 0, Intent(ACTION_USB_PERMISSION).setPackage(packageName), flags
                )
                usbManager.requestPermission(device, permissionIntent)
            }
        }
    }

    private fun openUsbDevice(device: UsbDevice) {
        val key = deviceKey(device)
        if (!openUsbDeviceKeys.add(key)) return
        executor.execute { openUsbDeviceBackground(device, key) }
    }

    private fun openUsbDeviceBackground(device: UsbDevice, key: String) {
        val usbManager = getSystemService(Context.USB_SERVICE) as UsbManager
        val connection = usbManager.openDevice(device) ?: run {
            LogBus.append("[ERROR] Could not open USB device")
            openUsbDeviceKeys.remove(key)
            return
        }
        val iface = device.getInterface(0)
        if (!connection.claimInterface(iface, true)) {
            LogBus.append("[ERROR] Could not claim USB interface")
            connection.close()
            openUsbDeviceKeys.remove(key)
            return
        }
        var epIn: UsbEndpoint? = null
        var epOut: UsbEndpoint? = null
        for (i in 0 until iface.endpointCount) {
            val endpoint = iface.getEndpoint(i)
            if (endpoint.type != UsbConstants.USB_ENDPOINT_XFER_BULK) continue
            if (endpoint.direction == UsbConstants.USB_DIR_IN) epIn = endpoint
            if (endpoint.direction == UsbConstants.USB_DIR_OUT) epOut = endpoint
        }
        if (epIn == null || epOut == null) {
            LogBus.append("[ERROR] USB device is missing bulk endpoints")
            connection.releaseInterface(iface)
            connection.close()
            openUsbDeviceKeys.remove(key)
            return
        }
        val stateKey = usbStateKey(key)
        val info = readUsbDescriptors(connection, device)
        val consoleId = info.consoleId ?: ConsoleId.generate()
        val versionSuffix = if (!info.version.isNullOrEmpty()) " v${info.version}" else ""
        val devSuffix = if (info.isDev) " (dev)" else ""
        val label = "${info.appName}$versionSuffix$devSuffix"
        ConnectionState.addUsb(stateKey, label, consoleId)
        LogBus.append("USB device connected: $label -> $consoleId")
        handleUsbClient(key, stateKey, connection, epIn, epOut, consoleId)
    }

    private data class UsbDescriptorInfo(
        val appName: String,
        val version: String?,
        val consoleId: String?,
        val isDev: Boolean,
    )

    private fun readUsbDescriptors(connection: UsbDeviceConnection, device: UsbDevice): UsbDescriptorInfo {
        val product = readUsbStringDescriptor(connection, 2) ?: try { device.productName } catch (e: Exception) { null }
        val serial = readUsbStringDescriptor(connection, 3) ?: try { device.serialNumber } catch (e: Exception) { null }

        val appName = when {
            product != null && product.contains("Leaflet") -> "Leaflet"
            product != null && product.contains("Goldleaf") -> "Goldleaf"
            else -> "Device"
        }

        if (serial.isNullOrEmpty()) {
            return UsbDescriptorInfo(appName, null, null, false)
        }

        var value = serial
        val isDev = value.endsWith("-dev")
        if (isDev) value = value.removeSuffix("-dev")

        val slash = value.indexOf('/')
        val version: String?
        val consoleId: String?
        if (slash >= 0) {
            version = value.substring(0, slash).ifEmpty { null }
            consoleId = value.substring(slash + 1).ifEmpty { null }
        } else {
            version = value.ifEmpty { null }
            consoleId = null
        }

        return UsbDescriptorInfo(appName, version, consoleId, isDev)
    }

    private fun readUsbStringDescriptor(connection: UsbDeviceConnection, index: Int): String? {
        if (index <= 0) return null
        val buffer = ByteArray(255)
        val languageId = 0x0409
        val requestType = 0x80
        val request = 0x06
        val value = (0x03 shl 8) or index
        val length = try {
            connection.controlTransfer(requestType, request, value, languageId, buffer, buffer.size, 1000)
        } catch (e: Exception) {
            -1
        }
        if (length < 2) return null
        val descriptorLength = buffer[0].toInt() and 0xFF
        val actualLength = minOf(length, descriptorLength)
        if (actualLength < 2) return null
        return try {
            val text = String(buffer, 2, actualLength - 2, Charsets.UTF_16LE)
            text.ifEmpty { null }
        } catch (e: Exception) {
            null
        }
    }

    private fun handleUsbClient(key: String, stateKey: String, connection: UsbDeviceConnection, epIn: UsbEndpoint, epOut: UsbEndpoint, initialConsoleId: String) {
        val channel = UsbCommandChannel(connection, epOut, epIn, "usb:$key")
        activeUsbChannels[key] = channel
        val session = CommandSession(applicationContext, initialConsoleId, transferListener(stateKey))
        try {
            while (channel.isAlive()) {
                val block = CommandBlock(channel)
                if (!block.isValid()) break
                if (!CommandDispatcher.dispatch(applicationContext, block, session)) break
            }
        } catch (e: Exception) {
            LogBus.append("[ERROR] USB session error: ${e.message}")
        } finally {
            session.dispose()
            channel.close()
            activeUsbChannels.remove(key, channel)
            openUsbDeviceKeys.remove(key)
            ConnectionState.removeUsb(stateKey)
            TransferState.clear(stateKey)
            LogBus.append("USB device disconnected")
        }
    }

    private fun transferListener(key: String) = object : ProgressListener {
        override fun onProgress(fileName: String, transferred: Long, total: Long) {
            TransferState.update(key, fileName, transferred, total)
        }
        override fun onIdle() {
            TransferState.clear(key)
        }
    }

    private fun deviceKey(device: UsbDevice): String = "${device.deviceId}"
    private fun usbStateKey(key: String): String = "usb:$key"

    companion object {
        const val ACTION_STOP = "io.github.quark.action.STOP"
        const val ACTION_USB_DEVICE_READY = "io.github.quark.action.USB_DEVICE_READY"
        const val ACTION_USB_PERMISSION = "io.github.quark.action.USB_PERMISSION"
        private const val NOTIFICATION_ID = 1

        val isRunning = MutableLiveData(false)

        fun start(context: Context) {
            val intent = Intent(context, QuarkService::class.java)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) context.startForegroundService(intent)
            else context.startService(intent)
        }

        fun stop(context: Context) {
            context.startService(Intent(context, QuarkService::class.java).setAction(ACTION_STOP))
        }

        fun notifyDeviceReady(context: Context, device: UsbDevice) {
            val intent = Intent(context, QuarkService::class.java)
                .setAction(ACTION_USB_DEVICE_READY)
                .putExtra(UsbManager.EXTRA_DEVICE, device)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) context.startForegroundService(intent)
            else context.startService(intent)
        }
    }
}
