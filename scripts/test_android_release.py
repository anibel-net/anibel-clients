import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("package_android", Path(__file__).with_name("package-android.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class AndroidReleaseTests(unittest.TestCase):
    def test_versions_sort_beta_numbers_numerically_and_stable_after_beta(self):
        names = ["0.1.0-beta.2", "0.1.0-beta.10", "0.1.0", "0.1.1-beta.1", "1.0.0"]
        self.assertEqual(names, sorted(reversed(names), key=release.version_key))
        for invalid in ("01.0.0", "1.0", "1.0.0-rc.1", "2147483648.0.0", "1.0.0-beta.2147483648"):
            with self.assertRaises(ValueError):
                release.version_key(invalid)

    def test_release_requires_higher_name_and_code_including_beta_to_stable(self):
        tag = "android-v1.0.0-beta.2"
        previous = [{"tag_name": tag, "draft": False, "assets": [{"name": release.METADATA_NAME,
            "browser_download_url": f"https://github.com/owner/repo/releases/download/{tag}/{release.METADATA_NAME}"}]}]
        metadata = json.dumps({"versionName": "1.0.0-beta.2", "versionCode": 7}).encode()
        with patch.object(release.urllib.request, "urlopen", side_effect=lambda *args, **kwargs: io.BytesIO(metadata)):
            release.check_previous(previous, "1.0.0", 8, "owner/repo")
            with self.assertRaisesRegex(ValueError, "Increase versionCode"):
                release.check_previous(previous, "1.0.0", 7, "owner/repo")
            with self.assertRaisesRegex(ValueError, "higher"):
                release.check_previous(previous, "1.0.0-beta.1", 8, "owner/repo")

    def test_windows_and_draft_releases_do_not_block_first_android_release(self):
        release.check_previous([{"tag_name": "windows-v99.0.0"}, {"tag_name": "android-v99.0.0", "draft": True}], "0.1.0", 1, "owner/repo")

    def test_unexpected_previous_metadata_host_is_rejected(self):
        previous = [{"tag_name": "android-v1.0.0", "assets": [{"name": release.METADATA_NAME, "browser_download_url": "https://example.invalid/metadata"}]}]
        with self.assertRaisesRegex(ValueError, "URL"):
            release.check_previous(previous, "1.0.1", 2, "owner/repo")


if __name__ == "__main__":
    unittest.main()
