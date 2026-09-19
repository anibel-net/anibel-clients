#!/usr/bin/env bash
# Package the installed MSYS2 UCRT64 libass and its dependency closure.
set -euo pipefail
export PATH="/ucrt64/bin:/usr/bin:$PATH"
root="$(cd "$(dirname "$0")/.." && pwd)"
output="$root/apps/windows/src/Assets/libass/x64"
mkdir -p "$output/licenses"
cp /ucrt64/bin/libass-9.dll "$output/"
while :; do
    added=0
    for binary in "$output/"*.dll; do
        while read -r dll; do
            if [[ ! -f "$output/$dll" && -f "/ucrt64/bin/$dll" ]]; then
                cp "/ucrt64/bin/$dll" "$output/"
                added=1
            fi
        done < <(objdump -p "$binary" | awk '/DLL Name:/ {print $3}')
    done
    [[ $added == 0 ]] && break
done
{
    echo 'libass runtime from MSYS2 UCRT64. Sources: https://packages.msys2.org/'
    for binary in "$output/"*.dll; do pacman -Qqo "/ucrt64/bin/$(basename "$binary")"; done | sort -u | while read -r package; do
        pacman -Q "$package"
        while read -r license; do
            [[ -f "$license" ]] || continue
            relative="${license#/ucrt64/share/licenses/}"
            mkdir -p "$output/licenses/$(dirname "$relative")"
            cp "$license" "$output/licenses/$relative"
        done < <(pacman -Qlq "$package" | grep '^/ucrt64/share/licenses/')
    done
} > "$output/licenses/BUILD.txt"
echo "libass runtime ready in $output"
