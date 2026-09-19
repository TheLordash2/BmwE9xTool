package com.thelordash2.bmwe9xtool.transport

interface BmwTransport {
    val connected: Boolean
    suspend fun connect(): Result<Unit>
    suspend fun readVin(): Result<String>
    suspend fun disconnect()
}
