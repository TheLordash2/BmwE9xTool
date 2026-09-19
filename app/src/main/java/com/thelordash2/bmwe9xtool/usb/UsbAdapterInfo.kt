package com.thelordash2.bmwe9xtool.usb

data class UsbAdapterInfo(
    val deviceId: Int,
    val vendorId: Int,
    val productId: Int,
    val manufacturer: String?,
    val product: String?,
    val serial: String?,
    val likelyFtdi: Boolean,
    val hasPermission: Boolean
) {
    fun describe(): String {
        val title = if (likelyFtdi) "Likely FTDI adapter" else "USB device"
        val permission = if (hasPermission) "granted" else "not granted"
        return buildString {
            append(title)
            append("\nVID:PID %04X:%04X".format(vendorId, productId))
            append("\nDevice ID: " + deviceId)
            if (manufacturer != null) append("\nManufacturer: " + manufacturer)
            if (product != null) append("\nProduct: " + product)
            if (serial != null) append("\nSerial: " + serial)
            append("\nPermission: " + permission)
        }
    }
}
