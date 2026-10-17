"""Create small local APKs for Android update certificate and package checks."""

import importlib.util
import os
from pathlib import Path
import tempfile

spec = importlib.util.spec_from_file_location("package_android", Path(__file__).with_name("package-android.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


def main():
    env = dict(os.environ)
    sdk = Path(env["ANDROID_HOME"])
    tools = sdk / "build-tools/37.0.0"
    aapt = tools / ("aapt2.exe" if os.name == "nt" else "aapt2")
    signer = tools / ("apksigner.bat" if os.name == "nt" else "apksigner")
    debug_key = Path.home() / ".android/debug.keystore"
    if not debug_key.is_file():
        raise ValueError("Build assembleDebug first to create the debug keystore.")
    _, code = release.read_version()
    if code >= 2_100_000_000:
        raise ValueError("The version code has no room for a newer test APK.")
    output = release.ROOT / "apps/android/app/build/update-fixtures"
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="anibel-update-fixtures-") as temporary:
        temporary = Path(temporary)
        other_key = temporary / "other.jks"
        release.run_tool(["keytool", "-genkeypair", "-noprompt", "-keystore", other_key,
                          "-alias", "fixture", "-storepass", "fixture-password", "-keypass", "fixture-password",
                          "-keyalg", "RSA", "-validity", "365", "-dname", "CN=Local test fixture"], env)
        for name, package, key, alias, password in (
            ("valid-update", "net.anibel.app", debug_key, "androiddebugkey", "android"),
            ("wrong-signer", "net.anibel.app", other_key, "fixture", "fixture-password"),
            ("wrong-package", "net.anibel.other", debug_key, "androiddebugkey", "android"),
        ):
            manifest = temporary / "AndroidManifest.xml"
            manifest.write_text(f'<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="{package}"><application android:hasCode="false" /></manifest>', encoding="utf-8")
            unsigned = temporary / f"{name}.apk"
            release.run_tool([aapt, "link", "--manifest", manifest, "-I", sdk / "platforms/android-37.2/android.jar",
                              "--version-code", str(code + 1), "--version-name", "local-update-test",
                              "--min-sdk-version", "26", "--target-sdk-version", "37", "-o", unsigned], env)
            release.run_tool([signer, "sign", "--ks", key, "--ks-key-alias", alias,
                              "--ks-pass", f"pass:{password}", "--key-pass", f"pass:{password}",
                              "--v4-signing-enabled", "false", "--out", output / f"{name}.apk", unsigned], env)
    print("Created three local update test APKs. No release signing key was used.")


if __name__ == "__main__":
    main()
