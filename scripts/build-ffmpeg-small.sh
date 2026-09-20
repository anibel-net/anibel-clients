#!/usr/bin/env bash
# Run with MSYS2 UCRT64 bash. Packages: gcc, pkgconf, libxml2, zlib, nasm, make.
set -euo pipefail
export PATH="/ucrt64/bin:/usr/bin:$PATH"
root="$(cd "$(dirname "$0")/.." && pwd)"
work="$root/artifacts/ffmpeg-small"
version=7.1.5
archive="ffmpeg-$version.tar.xz"
mkdir -p "$work"
cd "$work"
if [[ ! -f "$archive" ]]; then
    curl --fail --location "https://ffmpeg.org/releases/$archive" -o "$archive"
fi
echo "de668509caf9e35e3cd162473441fdb29538c6d96ed080292b3cf9e6fc5d558f  $archive" | sha256sum --check
if [[ ! -d "ffmpeg-$version" ]]; then
    tar -xf "$archive"
fi
mkdir -p build
cd build
"../ffmpeg-$version/configure" \
    --prefix="$work/install" --target-os=mingw32 --arch=x86_64 \
    --enable-shared --disable-static --enable-small --disable-debug \
    --disable-doc --disable-autodetect --disable-everything \
    --disable-ffplay --disable-avdevice --disable-postproc \
    --enable-schannel --enable-libxml2 --enable-zlib \
    --enable-protocol=file,http,https,tcp,tls,crypto,data \
    --enable-demuxer=hls,dash,mov,mpegts,matroska,aac,mp3,flac,ogg,wav,ass,srt,webvtt \
    --enable-muxer=matroska,ass \
    --enable-parser=h264,hevc,av1,vp9,aac,aac_latm,mpegaudio,ac3,opus,vorbis,flac \
    --enable-decoder=h264,hevc,av1,vp9,aac,mp3,ac3,eac3,opus,vorbis,flac,ass,srt,webvtt,movtext \
    --enable-encoder=srt,ass \
    --enable-bsf=aac_adtstoasc,extract_extradata,h264_mp4toannexb,hevc_mp4toannexb \
    --extra-ldflags=-static-libgcc
make -j"${NUMBER_OF_PROCESSORS:-4}"
make install
runtime="$work/runtime"
mkdir -p "$runtime/licenses"
cp "$work/install/bin/"*.exe "$work/install/bin/"*.dll "$runtime/"
# Copy the dependency closure, excluding Windows system libraries.
while :; do
    added=0
    for binary in "$runtime/"*.exe "$runtime/"*.dll; do
        while read -r dll; do
            if [[ ! -f "$runtime/$dll" && -f "/ucrt64/bin/$dll" ]]; then
                cp "/ucrt64/bin/$dll" "$runtime/"
                added=1
            fi
        done < <(objdump -p "$binary" | awk '/DLL Name:/ {print $3}')
    done
    [[ $added == 0 ]] && break
done
cp "$work/ffmpeg-$version/COPYING.LGPLv2.1" "$runtime/licenses/FFmpeg-LICENSE.txt"
cp "$root/scripts/build-ffmpeg-small.sh" "$runtime/licenses/build-ffmpeg-small.sh"
for package in libxml2 libiconv zlib libwinpthread; do
    mkdir -p "$runtime/licenses/$package"
    cp -r "/ucrt64/share/licenses/$package/." "$runtime/licenses/$package/"
done
{
    echo "FFmpeg $version, minimal shared build for Anibel playback and MKV downloads."
    echo "Source: https://ffmpeg.org/releases/$archive"
    echo "Build recipe: build-ffmpeg-small.sh (included)."
    echo "MSYS2 dependencies and source packages: https://packages.msys2.org/"
    pacman -Q mingw-w64-ucrt-x86_64-gcc mingw-w64-ucrt-x86_64-libxml2 mingw-w64-ucrt-x86_64-libiconv mingw-w64-ucrt-x86_64-zlib mingw-w64-ucrt-x86_64-libwinpthread
} > "$runtime/licenses/FFmpeg-README.txt"
printf '\nBuilt minimal runtime in %s\n' "$runtime"
