package net.anibel.app

import org.junit.Assert.assertEquals
import org.junit.Test

class CoreCommandTest {
    @Test fun wireNamesMatchSharedContract() {
        val text = checkNotNull(javaClass.getResourceAsStream("/commands.json")).bufferedReader().use { it.readText() }
        val names = Regex("\"([^\"]+)\"").findAll(text).map { it.groupValues[1] }.toSet()
        assertEquals(names, CoreCommand.entries.map { it.wire }.toSet())
    }
}
