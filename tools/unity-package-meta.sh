#!/usr/bin/env bash
# Writes the missing Unity .meta files of the WildCat UPM package
# (unity-sdk/Packages/com.noopsoftworks.wildcat).
#
# Unity needs a .meta file with a GUID next to every file and folder of a package, and a package
# installed from git is read only, so the .meta files must be committed. Opening the project in
# Unity creates them too; this script is for changes made without Unity. It never touches an
# existing .meta file. New GUIDs are the MD5 of "wildcat-package:" plus the path relative to the
# package folder, so two people adding the same file get the same GUID.
#
# Left out: Runtime/Protocol (tools/sync-protocol.sh writes it with its own metas),
# Documentation~ and the Samples~ folder itself (Unity does not import folders ending in ~;
# the files inside a sample get metas because Unity copies them into Assets when the sample is
# imported). WildCat.Sdk.Tests/PackageLayoutTests fails when a .meta is missing.
#
# Usage, from anywhere: tools/unity-package-meta.sh
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package="$repo_root/unity-sdk/Packages/com.noopsoftworks.wildcat"

if command -v md5sum >/dev/null 2>&1; then
    md5_hex() { printf '%s' "$1" | md5sum | cut -c1-32; }
else
    md5_hex() { printf '%s' "$1" | md5 -q; }
fi

tail_lines='  userData: \n  assetBundleName: \n  assetBundleVariant: \n'

meta_for() { # relative-path is-directory
    local rel="$1" guid
    guid="$(md5_hex "wildcat-package:$rel")"
    if [ "$2" = "dir" ]; then
        printf "fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n$tail_lines" "$guid"
        return
    fi
    case "$rel" in
        *.cs)
            printf "fileFormatVersion: 2\nguid: %s\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n$tail_lines" "$guid" ;;
        *.asmdef)
            printf "fileFormatVersion: 2\nguid: %s\nAssemblyDefinitionImporter:\n  externalObjects: {}\n$tail_lines" "$guid" ;;
        package.json)
            printf "fileFormatVersion: 2\nguid: %s\nPackageManifestImporter:\n  externalObjects: {}\n$tail_lines" "$guid" ;;
        *.asset)
            printf "fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n$tail_lines" "$guid" ;;
        *.jslib)
            printf "fileFormatVersion: 2\nguid: %s\nPluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n  - first:\n      Any: \n    second:\n      enabled: 0\n      settings: {}\n  - first:\n      Editor: Editor\n    second:\n      enabled: 0\n      settings:\n        DefaultValueInitialized: true\n  - first:\n      WebGL: WebGL\n    second:\n      enabled: 1\n      settings: {}\n$tail_lines" "$guid" ;;
        *.md|*.txt|*.json)
            printf "fileFormatVersion: 2\nguid: %s\nTextScriptImporter:\n  externalObjects: {}\n$tail_lines" "$guid" ;;
        *)
            printf "fileFormatVersion: 2\nguid: %s\nDefaultImporter:\n  externalObjects: {}\n$tail_lines" "$guid" ;;
    esac
}

written=0
while IFS= read -r -d '' path; do
    rel="${path#"$package"/}"
    case "$rel" in
        *.meta|.*|*/.*) continue ;;
        Runtime/Protocol|Runtime/Protocol/*) continue ;;
        Documentation~|Documentation~/*|Samples~) continue ;;
    esac
    [ -e "$path.meta" ] && continue
    if [ -d "$path" ]; then kind=dir; else kind=file; fi
    meta_for "$rel" "$kind" > "$path.meta"
    echo "wrote $rel.meta"
    written=$((written + 1))
done < <(find "$package" -mindepth 1 -print0 | LC_ALL=C sort -z)

echo "unity-package-meta: wrote $written .meta file(s)"
