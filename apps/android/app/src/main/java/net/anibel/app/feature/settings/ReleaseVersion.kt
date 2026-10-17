package net.anibel.app

/** The Android release tags support stable versions and beta.N versions only. */
internal data class ReleaseVersion(val major: Int, val minor: Int, val patch: Int, val beta: Int?) : Comparable<ReleaseVersion> {
    override fun compareTo(other: ReleaseVersion): Int =
        compareValuesBy(this, other, { it.major }, { it.minor }, { it.patch }, { it.beta == null }, { it.beta ?: 0 })

    override fun toString(): String = "$major.$minor.$patch" + (beta?.let { "-beta.$it" } ?: "")

    companion object {
        fun parse(name: String): ReleaseVersion? {
            val match = Regex("(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-beta\\.(0|[1-9][0-9]*))?").matchEntire(name) ?: return null
            val parts = match.groupValues.drop(1).take(3).map { it.toIntOrNull() ?: return null }
            val beta = match.groupValues[4].takeIf(String::isNotEmpty)?.let { it.toIntOrNull() ?: return null }
            return ReleaseVersion(parts[0], parts[1], parts[2], beta)
        }
    }
}
