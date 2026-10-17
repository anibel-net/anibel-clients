import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.compose.compiler)
}

val appVersion = Properties().apply {
    rootProject.file("version.properties").inputStream().use { load(it) }
}
val releaseVersion = appVersion.getProperty("versionName")
val versionParts = requireNotNull(Regex("(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-beta\\.(0|[1-9][0-9]*))?").matchEntire(releaseVersion))
require(versionParts.groupValues.drop(1).filter(String::isNotEmpty).all { it.toIntOrNull() != null })
val releaseCode = appVersion.getProperty("versionCode").toInt()
require(releaseCode in 1..2_100_000_000)

android {
    ndkVersion = "29.0.14206865"
    namespace = "net.anibel.app"
    compileSdk { version = release(37) { minorApiLevel = 2 } }
    buildToolsVersion = "37.0.0"

    defaultConfig {
        applicationId = "net.anibel.app"
        minSdk = 26
        targetSdk = 37
        versionCode = releaseCode
        versionName = releaseVersion
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures { compose = true; buildConfig = true }
    sourceSets.getByName("androidTest").assets.srcDir(layout.buildDirectory.dir("playback-fixtures").get().asFile)
    sourceSets.getByName("androidTest").assets.directories.add(layout.buildDirectory.dir("update-fixtures").get().asFile.path)
    sourceSets.getByName("test").resources.srcDir(rootProject.file("../../crates/anibel-core/tests/fixtures"))
    sourceSets.getByName("main").jniLibs.directories.add(layout.buildDirectory.dir("rustJniLibs").get().asFile.path)
    sourceSets.getByName("main").assets.directories.add(layout.buildDirectory.dir("noticeAssets").get().asFile.path)
}

val copyNotices = tasks.register<Copy>("copyNotices") {
    from(rootProject.file("../../LICENSE")) { rename { "LICENSE.txt" } }
    from(rootProject.file("THIRD-PARTY-NOTICES.txt"))
    into(layout.buildDirectory.dir("noticeAssets"))
}

val buildRust = tasks.register<Exec>("buildRust") {
    val repository = rootProject.file("../..")
    val libraries = layout.buildDirectory.dir("rustJniLibs")
    workingDir(repository)
    environment("ANDROID_NDK_HOME", androidComponents.sdkComponents.sdkDirectory.get().dir("ndk/${android.ndkVersion}").asFile)
    commandLine("cargo", "ndk", "-t", "x86_64", "-t", "arm64-v8a", "-o", libraries.get().asFile,
        "build", "-p", "anibel-core", "--release", "--locked")
    inputs.files(fileTree(repository.resolve("crates")), repository.resolve("Cargo.toml"),
        repository.resolve("Cargo.lock"), repository.resolve("rust-toolchain.toml"))
    outputs.dir(libraries)
}
tasks.named("preBuild") { dependsOn(buildRust, copyNotices) }

kotlin { jvmToolchain(17) }

dependencies {
    testImplementation("junit:junit:4.13.2")
    implementation("io.github.peerless2012:ass-media:0.5.1")
    implementation("io.github.peerless2012:ass-kt:0.5.1")
    implementation(libs.androidx.documentfile)
    implementation(libs.androidx.media3.exoplayer)
    implementation(libs.androidx.media3.session)
    implementation(libs.androidx.media3.hls)
    implementation(libs.androidx.media3.dash)
    implementation(libs.androidx.media3.ui)
    implementation(libs.androidx.media3.ui.compose.material3)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.coil.compose)
    implementation(libs.coil.network)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.tv.material)
    implementation(libs.androidx.compose.ui.tooling.preview)
    debugImplementation(libs.androidx.compose.ui.tooling)
    androidTestImplementation(platform(libs.androidx.compose.bom))
    androidTestImplementation(libs.androidx.compose.ui.test.junit4)
    androidTestImplementation("androidx.test.espresso:espresso-core:3.7.0")
    androidTestImplementation(libs.androidx.test.runner)
    androidTestImplementation(libs.androidx.test.junit)
}
