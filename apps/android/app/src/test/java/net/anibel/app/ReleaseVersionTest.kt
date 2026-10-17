package net.anibel.app

import org.junit.Assert.*
import org.junit.Test

class ReleaseVersionTest {
    @Test fun betaAndStableVersionsSortInUpdateOrder() {
        val names = listOf("0.1.0-beta.2", "0.1.0-beta.10", "0.1.0", "0.1.1-beta.1", "0.1.1", "0.10.0", "1.0.0")
        val versions = names.map { checkNotNull(ReleaseVersion.parse(it)) }
        assertEquals(versions, versions.reversed().sorted())
        assertEquals(names, versions.map { it.toString() })
    }

    @Test fun unsupportedAndOverflowingVersionsAreRejected() {
        listOf("windows-v1.0.0", "1.0", "01.0.0", "1.0.0-rc.1", "1.0.0-beta.01", "2147483648.0.0", "1.0.0-beta.2147483648").forEach {
            assertNull(it, ReleaseVersion.parse(it))
        }
        assertNotNull(ReleaseVersion.parse("2147483647.0.0-beta.2147483647"))
    }
}
