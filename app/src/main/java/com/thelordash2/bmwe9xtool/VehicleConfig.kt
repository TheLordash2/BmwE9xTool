package com.thelordash2.bmwe9xtool

object VehicleConfig {
    const val PLATFORM = "BMW E8x/E9x"
    const val DATEN_FAMILY = "E89"

    @Volatile
    private var expectedVin: String? = null

    fun configureExpectedVin(vin: String?) {
        expectedVin = vin?.trim()?.uppercase()?.takeIf { it.isNotEmpty() }
    }

    fun vinMatches(readVin: String): Boolean =
        expectedVin != null && readVin.trim().uppercase() == expectedVin
}
