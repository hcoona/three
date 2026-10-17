#!/usr/bin/env bash
# Bootstrap the committed control application; its typed caller owns full composition.
set -Eeuo pipefail
operator_directory=$PWD
repository=''
directory=''
tool_data='-'
while (($#)); do
  case "$1" in
  --repository | --directory | --mise-data-directory)
    if (($# < 2)); then
      echo "Missing value for $1" >&2
      exit 2
    fi
    case "$1" in
    --repository) repository=$2 ;;
    --directory) directory=$2 ;;
    --mise-data-directory) tool_data=$2 ;;
    esac
    shift 2
    ;;
  *)
    echo "Unsupported option: $1" >&2
    exit 2
    ;;
  esac
done
if [[ -z $repository ]]; then repository=$(git rev-parse --show-toplevel); fi
[[ $(uname -s) == Linux ]] || {
  echo 'Native Python full supports Linux.' >&2
  exit 2
}
repository=$(realpath "$repository")
[[ -z $(git -C "$repository" diff HEAD --name-only) ]] || {
  echo 'Commit tracked changes before testing committed HEAD.' >&2
  exit 2
}
candidate=$(git -C "$repository" rev-parse HEAD)
if [[ -z $directory ]]; then
  # Results are retained for inspection, so keep them off memory-backed /tmp.
  state_root=${XDG_CACHE_HOME:-$HOME/.cache}/three/python-full
  mkdir -p "$state_root"
  directory=$(mktemp -d "$state_root/$(date -u +%Y%m%dT%H%M%SZ)-XXXXXXXX")/state
fi
directory=$(realpath -m "$directory")
[[ $directory != "$repository" && $directory != "$repository/"* && $repository != "$directory/"* ]] || {
  echo 'Full-group state must be outside the source checkout.' >&2
  exit 2
}
[[ ! -e $directory ]] || {
  echo 'Full-group state already exists.' >&2
  exit 2
}
mkdir -p "$directory"
echo "Full Python group at commit $candidate; results: $directory"
git -C "$repository" worktree add --detach "$directory/control-checkout" "$candidate"
cd "$directory/control-checkout"
pwsh -NoLogo -NoProfile -File eng/scripts/Bootstrap-CiControl.ps1 \
  -Repository "$PWD" -Directory "$directory/control-build"
dotnet "$directory/control-build/transfer/control/WorkflowDelivery.dll" ci caller-local-python \
  "$repository" "$directory" "$directory/control-build/transfer" "$operator_directory" "$tool_data" \
  "$(command -v uv)" "$(command -v python)" "$(command -v dotnet)" \
  "$(command -v mise || printf '%s' '-')" \
  "$(command -v node || printf '%s' '-')" "$(command -v pwsh || printf '%s' '-')"
echo "Retained full Python group state: $directory"
printf 'Remove it when finished: git -C %q worktree remove --force %q && rm -rf %q\n' \
  "$repository" "$directory/control-checkout" "$(dirname "$directory")"
