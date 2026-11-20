#!/bin/sh
set -eu
project_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
command=${1:-}
if [ "$#" -gt 0 ]; then shift; fi
# SDK and test-runner selection follows cwd; project paths remain absolute.
export DirectoryBuildPropsPath="$project_root/configs/dotnet/Directory.Build.props"
export DirectoryPackagesPropsPath="$project_root/configs/dotnet/Directory.Packages.props"
cd "$project_root/configs/dotnet"
case "$command" in
  restore|build|format)
    exec dotnet "$command" "$project_root/src/core/LifeTracker.slnx" "$@"
    ;;
  publish-host)
    exec dotnet publish "$project_root/src/core/LifeTracker.Host" "$@"
    ;;
  test)
    exec dotnet test --solution "$project_root/src/core/LifeTracker.slnx" "$@"
    ;;
  run)
    exec dotnet run --project "$project_root/src/core/LifeTracker.Host" "$@"
    ;;
  --version|--info)
    exec dotnet "$command" "$@"
    ;;
  *)
    echo 'Usage: sh tools/dotnet.sh {restore|build|format|test|run|publish-host|--version|--info} [options]' >&2
    exit 2
    ;;
esac
