#!/usr/bin/env bash
# Copies the shared wire protocol into the Unity SDK.
#
# Source: backend/WildCat.Protocol/**/*.cs, except ServerOnly/ (messages that carry a developer
# secret and must never ship in a game client) and the obj/ and bin/ build folders.
# Target: unity-sdk/Packages/com.noopsoftworks.wildcat/Runtime/Protocol/ in the Unity package,
# which is deleted and written again, so files removed from the source disappear from the copy too.
# The copy also gets its assembly definition (WildCat.Protocol.asmdef, from
# tools/protocol-copy-asmdef.json) and a README saying it is generated.
#
# Unity needs a .meta file with a GUID next to every file and folder. The GUIDs are the MD5 of
# "wildcat-protocol:" plus the path relative to the target folder ("" for the folder itself), so
# they never change between runs or machines and running the script twice changes nothing.
# tools/sync-protocol.ps1 does the same on Windows, and WildCat.Tests/ProtocolSyncTests fails when
# the copy differs from the source.
#
# Usage, from anywhere: tools/sync-protocol.sh
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="$repo_root/backend/WildCat.Protocol"
target_dir="$repo_root/unity-sdk/Packages/com.noopsoftworks.wildcat/Runtime/Protocol"
readme="$repo_root/tools/protocol-copy-readme.md"
asmdef="$repo_root/tools/protocol-copy-asmdef.json"

if command -v md5sum >/dev/null 2>&1; then
    md5_hex() { printf '%s' "$1" | md5sum | cut -c1-32; }
elif command -v md5 >/dev/null 2>&1; then
    md5_hex() { printf '%s' "$1" | md5 -q; }
else
    echo "sync-protocol: needs md5sum or md5" >&2
    exit 1
fi

guid_for() {
    md5_hex "wildcat-protocol:$1"
}

write_script_meta() { # path-of-meta relative-path
    printf 'fileFormatVersion: 2\nguid: %s\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' \
        "$(guid_for "$2")" > "$1"
}

write_text_meta() { # path-of-meta relative-path
    printf 'fileFormatVersion: 2\nguid: %s\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' \
        "$(guid_for "$2")" > "$1"
}

write_asmdef_meta() { # path-of-meta relative-path
    printf 'fileFormatVersion: 2\nguid: %s\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' \
        "$(guid_for "$2")" > "$1"
}

write_folder_meta() { # path-of-meta relative-path
    printf 'fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' \
        "$(guid_for "$2")" > "$1"
}

rm -rf "$target_dir"
mkdir -p "$target_dir"
write_folder_meta "$target_dir.meta" ""

count=0
while IFS= read -r -d '' file; do
    rel="${file#"$source_dir"/}"
    case "$rel" in
        ServerOnly/*|obj/*|bin/*) continue ;;
    esac
    # Create each folder on the way down, with its .meta.
    dir="$(dirname "$rel")"
    if [ "$dir" != "." ]; then
        sub=""
        IFS='/' read -r -a parts <<< "$dir"
        for part in "${parts[@]}"; do
            sub="${sub:+$sub/}$part"
            if [ ! -d "$target_dir/$sub" ]; then
                mkdir "$target_dir/$sub"
                write_folder_meta "$target_dir/$sub.meta" "$sub"
            fi
        done
    fi
    cp "$file" "$target_dir/$rel"
    write_script_meta "$target_dir/$rel.meta" "$rel"
    count=$((count + 1))
done < <(find "$source_dir" -type f -name '*.cs' -print0 | LC_ALL=C sort -z)

cp "$readme" "$target_dir/README.md"
write_text_meta "$target_dir/README.md.meta" "README.md"
cp "$asmdef" "$target_dir/WildCat.Protocol.asmdef"
write_asmdef_meta "$target_dir/WildCat.Protocol.asmdef.meta" "WildCat.Protocol.asmdef"

echo "sync-protocol: copied $count files to ${target_dir#"$repo_root"/}"
