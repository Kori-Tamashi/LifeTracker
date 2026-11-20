#!/bin/sh
set -eu
mode=${1:-lint}
case "$mode" in
  lint) action='lint --strict' ;;
  format) action='format --in-place' ;;
  *) echo 'Usage: sh tools/format_swift.sh [lint|format]' >&2; exit 2 ;;
esac
# Explicit paths exclude SwiftPM generated sources and build caches.
xcrun swift-format $action --configuration configs/swift-format.json --recursive \
  src/macos/Package.swift src/macos/Sources src/macos/Tests
