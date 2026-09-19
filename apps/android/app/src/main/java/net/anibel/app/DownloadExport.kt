package net.anibel.app

import android.content.Context
import android.net.Uri
import androidx.documentfile.provider.DocumentFile
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.util.UUID

internal suspend fun exportDownload(context: Context, core: CoreClient, id: String, destination: Uri) = withContext(Dispatchers.IO) {
    val temporary = File(context.cacheDir, "export-${UUID.randomUUID()}")
    check(temporary.mkdirs()) { ui(R.string.export_prepare_error) }
    try {
        val exported = File(core.call("downloadExport", json("id" to id, "destination" to temporary.path)).getString("path"))
        check(exported.canonicalFile.toPath().startsWith(temporary.canonicalFile.toPath())) { ui(R.string.export_invalid_path) }
        val root = checkNotNull(DocumentFile.fromTreeUri(context, destination)) { ui(R.string.export_folder_error) }
        check(root.findFile(exported.name) == null) { ui(R.string.export_exists) }
        val target = checkNotNull(root.createDirectory(exported.name)) { ui(R.string.export_create_error) }
        try {
            fun copy(source: File, folder: DocumentFile) {
                source.listFiles()?.forEach { child ->
                    check(child.canonicalFile.toPath().startsWith(temporary.canonicalFile.toPath())) { ui(R.string.export_invalid_file) }
                    if (child.isDirectory) copy(child, checkNotNull(folder.createDirectory(child.name)))
                    else {
                        val output = checkNotNull(folder.createFile("application/octet-stream", child.name))
                        checkNotNull(context.contentResolver.openOutputStream(output.uri)).use { sink -> child.inputStream().use { it.copyTo(sink) } }
                    }
                }
            }
            copy(exported, target)
        } catch (failure: Exception) { target.delete(); throw failure }
    } finally { temporary.deleteRecursively() }
}
