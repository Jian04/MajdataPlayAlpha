#!/bin/bash
set -euo pipefail

project_dir="$(cd "${1:-$(dirname "$0")}" && pwd)"
output_dir="${2:-$project_dir/Build}"
if [ ! -d "$project_dir/Unity-iPhone.xcodeproj" ]; then
    echo "请指定包含 Unity-iPhone.xcodeproj 的文件夹。" >&2
    exit 1
fi
mkdir -p "$output_dir"
output_dir="$(cd "$output_dir" && pwd)"
xcodebuild -project "$project_dir/Unity-iPhone.xcodeproj" -scheme Unity-iPhone \
    -configuration Release -sdk iphoneos -destination 'generic/platform=iOS' \
    -derivedDataPath "$output_dir/DerivedData" \
    -archivePath "$output_dir/MajdataPlayAlpha.xcarchive" \
    CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO archive 2>&1 | tee "$output_dir/xcodebuild.log"

set -- "$output_dir/MajdataPlayAlpha.xcarchive/Products/Applications/"*.app
if [ "$#" -ne 1 ] || [ ! -d "$1" ]; then
    echo "归档中没有找到唯一的应用。" >&2
    exit 1
fi
stage="$(mktemp -d "${TMPDIR:-/tmp}/majdata-ipa.XXXXXX")"
trap 'rm -rf "$stage"' EXIT
mkdir "$stage/Payload"
ditto --norsrc --noextattr "$1" "$stage/Payload/MajdataPlayAlpha.app"
ditto -c -k --norsrc --noextattr --keepParent "$stage/Payload" "$output_dir/MajdataPlayAlpha-alpha.0.6.1-unsigned.ipa"
echo "未签名 IPA：$output_dir/MajdataPlayAlpha-alpha.0.6.1-unsigned.ipa"
echo "用户需要使用自己的 Apple 账号重新签名后安装。"
