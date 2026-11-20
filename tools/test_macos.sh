#!/bin/sh
set -eu

# Command Line Tools ships Testing.framework but SwiftPM does not always add its paths.
# Derive paths from the selected toolchain; do not require a particular developer machine.
developer_dir=$(xcode-select -p)
frameworks="$developer_dir/Library/Developer/Frameworks"
runtime="$developer_dir/Library/Developer/usr/lib"
case "$developer_dir" in
  */CommandLineTools)
    swift test --package-path src/macos --disable-xctest \
      -Xswiftc -F -Xswiftc "$frameworks" \
      -Xlinker -F -Xlinker "$frameworks" \
      -Xlinker -rpath -Xlinker "$frameworks" \
      -Xlinker -rpath -Xlinker "$runtime" "$@"
    ;;
  *) swift test --package-path src/macos "$@" ;;
esac
