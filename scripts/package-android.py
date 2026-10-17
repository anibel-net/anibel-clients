"""Sign and check the Android APK. Only the standard Python library is required."""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import urllib.request
import zipfile


ROOT = Path(__file__).resolve().parents[1]
APK_NAME = "Anibel-Android.apk"
METADATA_NAME = "android-update.json"
VERSION_PATTERN = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-beta\.(0|[1-9][0-9]*))?")
SECRET_NAMES = ("ANDROID_KEYSTORE_BASE64", "ANDROID_KEYSTORE_PASSWORD", "ANDROID_KEY_ALIAS", "ANDROID_KEY_PASSWORD")


def version_key(name):
    match = VERSION_PATTERN.fullmatch(name)
    if not match:
        raise ValueError("Use X.Y.Z or X.Y.Z-beta.N for the Android version.")
    major, minor, patch, beta = match.groups()
    numbers = tuple(int(value) for value in (major, minor, patch))
    if any(value > 2_147_483_647 for value in numbers) or (beta and int(beta) > 2_147_483_647):
        raise ValueError("An Android version component exceeds the supported integer range.")
    return (*numbers, beta is None, int(beta) if beta else 0)


def read_version():
    values = dict(line.split("=", 1) for line in (ROOT / "apps/android/version.properties").read_text().splitlines() if "=" in line)
    version_key(values["versionName"])
    code = int(values["versionCode"])
    if not 1 <= code <= 2_100_000_000:
        raise ValueError("versionCode must be between 1 and 2100000000.")
    return values["versionName"], code


def run_tool(args, env):
    result = subprocess.run([str(arg) for arg in args], env=env, capture_output=True, text=True)
    if result.returncode:
        # Signing tools can echo arguments. Do not print their failure output.
        raise RuntimeError(f"{Path(args[0]).name} failed with exit code {result.returncode}.")
    return result.stdout


def check_previous(releases, version, code, repository):
    previous = []
    for release in releases:
        tag = release.get("tag_name", "")
        if release.get("draft") or not tag.startswith("android-v"):
            continue
        previous.append((version_key(tag.removeprefix("android-v")), release))
    if not previous:
        return
    newest_key, newest = max(previous, key=lambda item: item[0])
    if version_key(version) <= newest_key:
        raise ValueError("The tag version must be higher than every published Android release.")
    asset = next((asset for asset in newest["assets"] if asset["name"] == METADATA_NAME), None)
    if not asset:
        raise ValueError("The previous Android release has no update metadata.")
    expected = f"https://github.com/{repository}/releases/download/{newest['tag_name']}/{METADATA_NAME}"
    if asset["browser_download_url"] != expected:
        raise ValueError("The previous metadata URL does not match the release repository.")
    with urllib.request.urlopen(expected, timeout=30) as response:
        data = response.read(16_385)
    if len(data) > 16_384:
        raise ValueError("The previous update metadata is too large.")
    previous_metadata = json.loads(data)
    if previous_metadata["versionName"] != newest["tag_name"].removeprefix("android-v"):
        raise ValueError("The previous release metadata does not match its tag.")
    if code <= previous_metadata["versionCode"]:
        raise ValueError("Increase versionCode before creating the Android release.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="A new output directory; existing output is never replaced.")
    parser.add_argument("--tag")
    parser.add_argument("--secrets-file", type=Path)
    parser.add_argument("--previous-releases", type=Path, help="JSON from gh api --paginate --slurp.")
    parser.add_argument("--repository", default="anibel-net/anibel-clients")
    args = parser.parse_args()
    version, code = read_version()
    if args.tag and args.tag != f"android-v{version}":
        raise ValueError("The release tag must match apps/android/version.properties.")
    if args.previous_releases:
        pages = json.loads(args.previous_releases.read_text(encoding="utf-8"))
        check_previous([release for page in pages for release in page], version, code, args.repository)
    env = dict(os.environ)
    if args.secrets_file:
        values = dict(line.split("=", 1) for line in args.secrets_file.read_text(encoding="utf-8").splitlines() if line and not line.startswith("#"))
        env.update({name: values[name] for name in SECRET_NAMES})
    if any(not env.get(name) for name in SECRET_NAMES):
        raise ValueError("All four ANDROID signing secrets are required.")
    sdk = env.get("ANDROID_HOME") or env.get("ANDROID_SDK_ROOT")
    if not sdk:
        raise ValueError("Set ANDROID_HOME to the Android SDK directory.")
    tools = Path(sdk) / "build-tools/37.0.0"
    apksigner = tools / ("apksigner.bat" if os.name == "nt" else "apksigner")
    aapt = tools / ("aapt.exe" if os.name == "nt" else "aapt")
    source = args.apk.resolve(strict=True)
    badging = run_tool([aapt, "dump", "badging", source], env)
    if f"package: name='net.anibel.app' versionCode='{code}' versionName='{version}'" not in badging:
        raise ValueError("The APK package or version does not match version.properties.")
    with zipfile.ZipFile(source) as archive:
        for abi in ("arm64-v8a", "x86_64"):
            if f"lib/{abi}/libanibel_core.so" not in archive.namelist():
                raise ValueError(f"The APK does not include the Rust core for {abi}.")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    apk = output / APK_NAME
    # Keys stay outside build output and Gradle caches, and are deleted on exit.
    with tempfile.TemporaryDirectory(prefix="anibel-android-signing-") as temporary:
        key = Path(temporary) / "release.jks"
        key.write_bytes(base64.b64decode(env["ANDROID_KEYSTORE_BASE64"], validate=True))
        if os.name != "nt":
            key.chmod(0o600)
        run_tool([apksigner, "sign", "--ks", key, "--ks-key-alias", env["ANDROID_KEY_ALIAS"],
                  "--ks-pass", "env:ANDROID_KEYSTORE_PASSWORD", "--key-pass", "env:ANDROID_KEY_PASSWORD",
                  "--v4-signing-enabled", "false", "--out", apk, source], env)
        run_tool([apksigner, "verify", "--verbose", "--print-certs", apk], env)
        keytool = shutil.which("keytool")
        if not keytool:
            raise ValueError("The JDK keytool command is required.")
        certificate = output / "anibel-android-release-certificate.pem"
        run_tool([keytool, "-exportcert", "-rfc", "-keystore", key, "-alias", env["ANDROID_KEY_ALIAS"],
                  "-storepass:env", "ANDROID_KEYSTORE_PASSWORD", "-file", certificate], env)
    with apk.open("rb") as source_file:
        digest = hashlib.file_digest(source_file, "sha256").hexdigest()
    metadata = {"versionName": version, "versionCode": code, "apk": APK_NAME, "size": apk.stat().st_size, "sha256": digest}
    (output / METADATA_NAME).write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    (output / f"{APK_NAME}.sha256").write_text(f"{digest}  {APK_NAME}\n", encoding="ascii")
    shutil.copyfile(ROOT / "LICENSE", output / "LICENSE.txt")
    shutil.copyfile(ROOT / "apps/android/THIRD-PARTY-NOTICES.txt", output / "THIRD-PARTY-NOTICES.txt")
    print(f"Signed and verified Android {version} (versionCode {code}). Output: {output}")


if __name__ == "__main__":
    main()
