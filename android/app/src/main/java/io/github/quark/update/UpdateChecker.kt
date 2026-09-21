package io.github.quark.update

import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.content.FileProvider
import io.github.quark.BuildConfig
import io.github.quark.QuarkApp
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream

data class QuarkVersion(val major: Int, val minor: Int, val micro: Int) {

    fun newerThan(other: QuarkVersion): Boolean {
        if (major != other.major) return major > other.major
        if (minor != other.minor) return minor > other.minor
        return micro > other.micro
    }

    override fun toString() = "$major.$minor.$micro"

    companion object {
        fun tryParse(s: String): QuarkVersion? {
            val parts = s.split(".")
            if (parts.size < 2) return null
            val major = parts[0].toIntOrNull() ?: return null
            val minor = parts[1].toIntOrNull() ?: return null
            val micro = if (parts.size >= 3) parts[2].toIntOrNull() ?: 0 else 0
            return QuarkVersion(major, minor, micro)
        }
    }
}

data class UpdateInfo(
    val latestVersion: QuarkVersion,
    val assetName: String,
    val downloadUrl: String,
    val assetSize: Long,
)

object UpdateChecker {

    private const val API_URL = "https://api.github.com/repos/DefenderOfHyrule/Quark.NET/releases/latest"
    private const val ASSET_PREFIX = "Quark-android"

    private val http = OkHttpClient.Builder()
        .addInterceptor { chain ->
            chain.proceed(
                chain.request().newBuilder()
                    .header("User-Agent", "Quark/${BuildConfig.VERSION_NAME}")
                    .build()
            )
        }
        .build()

    fun currentVersion(): QuarkVersion =
        QuarkVersion.tryParse(BuildConfig.VERSION_NAME) ?: QuarkVersion(0, 0, 0)

    fun check(): UpdateInfo? {
        val req = Request.Builder().url(API_URL).build()
        val json = http.newCall(req).execute().use { resp ->
            if (!resp.isSuccessful) return null
            resp.body?.string() ?: return null
        }

        val root = JSONObject(json)
        val tagName = root.optString("tag_name", "")
        if (tagName.isEmpty()) return null

        val cleaned = tagName.trimStart('v', 'V')
        val latest = QuarkVersion.tryParse(cleaned) ?: return null
        if (!latest.newerThan(currentVersion())) return null

        val assets = root.optJSONArray("assets") ?: return null
        for (i in 0 until assets.length()) {
            val asset = assets.getJSONObject(i)
            val name = asset.optString("name", "")
            val url = asset.optString("browser_download_url", "")
            if (name.isEmpty() || url.isEmpty()) continue
            if (!name.startsWith(ASSET_PREFIX, ignoreCase = true)) continue
            if (!name.endsWith(".apk", ignoreCase = true)) continue

            val size = asset.optLong("size", 0L)
            return UpdateInfo(latest, name, url, size)
        }
        return null
    }

    private val updatesDir: File
        get() = File(QuarkApp.instance.cacheDir, "updates").also { it.mkdirs() }

    fun downloadApk(info: UpdateInfo, onProgress: (Int) -> Unit): File {
        val dest = File(updatesDir, info.assetName)
        val req = Request.Builder().url(info.downloadUrl).build()

        http.newCall(req).execute().use { resp ->
            if (!resp.isSuccessful) throw Exception("HTTP ${resp.code}")
            val body = resp.body ?: throw Exception("Empty response")
            val total = if (body.contentLength() > 0) body.contentLength() else info.assetSize
            var received = 0L

            body.byteStream().use { src ->
                FileOutputStream(dest).use { dst ->
                    val buf = ByteArray(81920)
                    var read: Int
                    while (src.read(buf).also { read = it } != -1) {
                        dst.write(buf, 0, read)
                        received += read
                        if (total > 0) onProgress((received * 100 / total).toInt())
                    }
                }
            }
        }
        onProgress(100)
        return dest
    }

    fun canRequestInstallPackages(context: Context): Boolean {
        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            context.packageManager.canRequestPackageInstalls()
        } else {
            true
        }
    }

    fun installIntent(context: Context, apkFile: File): Intent {
        val uri = FileProvider.getUriForFile(
            context, "${context.packageName}.fileprovider", apkFile
        )
        return Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
    }
}
