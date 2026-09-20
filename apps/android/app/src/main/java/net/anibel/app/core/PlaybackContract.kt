package net.anibel.app

import org.json.JSONObject

internal enum class PlaybackEvent(val wire: String) { Ready("ready"), Position("position"), Ended("ended"), Close("close") }
internal sealed interface PlaybackSource {
    data class Embed(val pageUrl: String) : PlaybackSource
    data class Video(val videoUrl: String) : PlaybackSource
}
internal data class OpenedPlayback(val sessionId: Long, val source: PlaybackSource,
    val subtitlePaths: List<String>, val configDirectory: String, val preferDub: Boolean)

internal suspend fun CoreClient.openPlayback(args: JSONObject): OpenedPlayback {
    val result = call(CoreCommand.PlaybackOpen, args)
    val intent = result.getJSONObject("intent")
    val source = if (intent.getString("kind") == "embed") PlaybackSource.Embed(intent.getString("pageUrl"))
        else PlaybackSource.Video(intent.getString("videoSrc"))
    return OpenedPlayback(result.getLong("sessionId"), source, result.optJSONArray("subtitlePaths").strings(),
        result.getString("configDirectory"), result.optBoolean("preferDub"))
}
internal suspend fun CoreClient.reportPlayback(session: Long, sequence: Long, event: PlaybackEvent,
    position: Double, duration: Double): Double? {
    val result = call(CoreCommand.PlaybackReport, json("sessionId" to session, "sequence" to sequence,
        "event" to event.wire, "position" to position, "duration" to duration))
    return if (result.isNull("seekTo")) null else result.getDouble("seekTo")
}
