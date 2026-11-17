package io.github.quark.service

import java.net.Inet4Address
import java.net.NetworkInterface

object NetworkInfo {

    fun resolveLocalIp(): String? {
        var ethernet: String? = null
        var wireless: String? = null
        var fallback: String? = null
        try {
            val interfaces = NetworkInterface.getNetworkInterfaces() ?: return null
            for (iface in interfaces) {
                if (!iface.isUp || iface.isLoopback) continue
                val name = iface.name.lowercase()
                for (address in iface.inetAddresses) {
                    if (address !is Inet4Address) continue
                    val ip = address.hostAddress ?: continue
                    when {
                        name.startsWith("eth") || name.startsWith("en") ||
                            name.startsWith("eno") || name.startsWith("enp") ->
                            ethernet = ethernet ?: ip
                        name.startsWith("wlan") || name.startsWith("wlp") ||
                            name.startsWith("wlo") || name.startsWith("wi") ->
                            wireless = wireless ?: ip
                        else -> fallback = fallback ?: ip
                    }
                }
            }
        } catch (e: Exception) { }
        return ethernet ?: wireless ?: fallback
    }
}
