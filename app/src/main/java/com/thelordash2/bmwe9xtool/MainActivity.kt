package com.thelordash2.bmwe9xtool

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbManager
import android.os.Build
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import com.thelordash2.bmwe9xtool.databinding.ActivityMainBinding
import com.thelordash2.bmwe9xtool.usb.UsbDeviceRepository

class MainActivity : AppCompatActivity() {
    companion object {
        private const val ACTION_USB_PERMISSION =
            "com.thelordash2.bmwe9xtool.USB_PERMISSION"
    }

    private lateinit var binding: ActivityMainBinding
    private lateinit var usbManager: UsbManager
    private lateinit var usbRepository: UsbDeviceRepository
    private val logger = SessionLogger()
    private var selectedDevice: UsbDevice? = null

    private val permissionReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent?.action != ACTION_USB_PERMISSION) return
            val granted = intent.getBooleanExtra(UsbManager.EXTRA_PERMISSION_GRANTED, false)
            logger.add("USB permission " + if (granted) "granted" else "denied")
            refreshLog()
            scanUsb()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        usbManager = getSystemService(USB_SERVICE) as UsbManager
        usbRepository = UsbDeviceRepository(usbManager)

        binding.textVehicle.text =
            "Configured VIN: " + VehicleConfig.EXPECTED_VIN + "\n" +
            "Platform: " + VehicleConfig.PLATFORM + " / " + VehicleConfig.DATEN_FAMILY

        binding.buttonScanUsb.setOnClickListener { scanUsb() }
        binding.buttonRequestPermission.setOnClickListener { requestSelectedPermission() }

        val filter = IntentFilter(ACTION_USB_PERMISSION)
        if (Build.VERSION.SDK_INT >= 33) {
            registerReceiver(permissionReceiver, filter, RECEIVER_NOT_EXPORTED)
        } else {
            @Suppress("DEPRECATION")
            registerReceiver(permissionReceiver, filter)
        }

        logger.add("Application started in read-only mode")
        refreshLog()
    }

    override fun onDestroy() {
        unregisterReceiver(permissionReceiver)
        super.onDestroy()
    }

    private fun scanUsb() {
        val devices = usbRepository.scan()
        selectedDevice = devices.firstOrNull { it.second.likelyFtdi }?.first
            ?: devices.firstOrNull()?.first

        if (devices.isEmpty()) {
            binding.textUsbStatus.text = "No USB devices detected."
            binding.buttonRequestPermission.isEnabled = false
            logger.add("USB scan: no devices")
            refreshLog()
            return
        }

        binding.textUsbStatus.text =
            devices.joinToString("\n\n") { it.second.describe() }

        val selected = selectedDevice
        binding.buttonRequestPermission.isEnabled =
            selected != null && !usbManager.hasPermission(selected)

        logger.add("USB scan: " + devices.size + " device(s)")
        refreshLog()
    }

    private fun requestSelectedPermission() {
        val device = selectedDevice ?: return
        if (usbManager.hasPermission(device)) {
            scanUsb()
            return
        }

        val flags = PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        val permissionIntent = PendingIntent.getBroadcast(
            this,
            0,
            Intent(ACTION_USB_PERMISSION).setPackage(packageName),
            flags
        )
        logger.add("Requesting USB permission")
        refreshLog()
        usbManager.requestPermission(device, permissionIntent)
    }

    private fun refreshLog() {
        binding.textLog.text = logger.text()
    }
}
