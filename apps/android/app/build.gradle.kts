plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.compose.compiler)
}

android {
    ndkVersion = "29.0.14206865"
    namespace = "net.anibel.app"
    compileSdk { version = release(37) { minorApiLevel = 2 } }
    buildToolsVersion = "37.0.0"

    defaultConfig {
        applicationId = "net.anibel.app"
        minSdk = 26
        targetSdk = 37
        versionCode = 1
        versionName = "0.1.0"
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures { compose = true }
    sourceSets.getByName("main").jniLibs.directories.add(layout.buildDirectory.dir("rustJniLibs").get().asFile.path)
}

val buildRust = tasks.register<Exec>("buildRust") {
    val repository = rootProject.file("../..")
    val libraries = layout.buildDirectory.dir("rustJniLibs")
    workingDir(repository)
    environment("ANDROID_NDK_HOME", androidComponents.sdkComponents.sdkDirectory.get().dir("ndk/${android.ndkVersion}").asFile)
    commandLine("cargo", "ndk", "-t", "x86_64", "-t", "arm64-v8a", "-o", libraries.get().asFile,
        "build", "-p", "anibel-core", "--release", "--locked")
    inputs.files(fileTree(repository.resolve("core")) { exclude("target/**") },
        fileTree(repository.resolve("crates")), repository.resolve("Cargo.toml"),
        repository.resolve("Cargo.lock"), repository.resolve("rust-toolchain.toml"))
    outputs.dir(libraries)
}
tasks.named("preBuild") { dependsOn(buildRust) }

kotlin { jvmToolchain(17) }

dependencies {
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
