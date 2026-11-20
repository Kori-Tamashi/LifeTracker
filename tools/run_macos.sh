#!/bin/sh
set -eu
project_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
: "${LIFETRACKER_RUNTIME_ROOT:?Set an absolute runtime root shared with the local host}"
case "$LIFETRACKER_RUNTIME_ROOT" in
  /*) ;;
  *) echo 'LIFETRACKER_RUNTIME_ROOT must be absolute.' >&2; exit 2 ;;
esac
export LIFETRACKER_HOST_DLL="${LIFETRACKER_HOST_DLL:-$project_root/src/core/LifeTracker.Host/bin/Debug/net10.0/LifeTracker.Host.dll}"
exec swift run --package-path "$project_root/src/macos" LifeTrackerDesktop
