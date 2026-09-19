package com.thelordash2.bmwe9xtool.usb

import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbManager

class UsbDeviceRepository(private val usbManager: UsbManager) {
    companion object {
        const val FTDI_VENDOR_ID = 0x0403
    }

    fun scan(): List<Pair<UsbDevice, UsbAdapterInfo>> =
        usbManager.deviceList.values.map { device ->
            val permitted = usbManager.hasPermission(device)
            device to UsbAdapterInfo(
                deviceId = device.deviceId,
                vendorId = device.vendorId,
                productId = device.productId,
                manufacturer = if (permitted) device.manufacturerName else null,
                product = if (permitted) device.productName else null,
                serial = if (permitted) runCatching { device.serialNumber }.getOrNull() else null,
                likelyFtdi = device.vendorId == FTDI_VENDOR_ID,
                hasPermission = permitted
            )
        }.sortedByDescending { it.second.likelyFtdi }
}
