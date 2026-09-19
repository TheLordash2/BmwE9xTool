package com.thelordash2.bmwe9xtool

import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class SessionLogger {
    private val lines = mutableListOf<String>()
    private val format = SimpleDateFormat("HH:mm:ss.SSS", Locale.US)

    @Synchronized
    fun add(message: String) {
        lines += format.format(Date()) + "  " + message
    }

    @Synchronized
    fun text(): String = lines.joinToString("\n")
}
