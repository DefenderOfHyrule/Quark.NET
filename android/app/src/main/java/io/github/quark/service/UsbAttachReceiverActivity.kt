package io.github.quark.service

import android.content.Intent
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbManager
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import io.github.quark.proto.Protocol

class UsbAttachReceiverActivity : AppCompatActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (intent.action == UsbManager.ACTION_USB_DEVICE_ATTACHED) {
            val device: UsbDevice? = intent.getParcelableExtra(UsbManager.EXTRA_DEVICE)
            if (device != null && device.vendorId == Protocol.USB_VENDOR_ID && device.productId == Protocol.USB_PRODUCT_ID) {
                QuarkService.notifyDeviceReady(this, device)
            }
        }
        finish()
    }
}
