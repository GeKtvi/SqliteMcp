#!/usr/bin/env bash
# Adds this folder (where the SqliteMcp binary lives) to your shell PATH.
# Does not move or copy the binary. Restart the shell afterward.
#
# Usage:
#   ./add-to-path.sh          # register
#   ./add-to-path.sh --remove # unregister

set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MARKER="# SqliteMcp PATH (managed by add-to-path.sh)"
LINE="export PATH=\"${DIR}:\$PATH\""
REMOVE=0

if [[ "${1:-}" == "--remove" || "${1:-}" == "-Remove" ]]; then
  REMOVE=1
fi

if [[ ! -x "${DIR}/SqliteMcp" && ! -f "${DIR}/SqliteMcp" ]]; then
  echo "warning: SqliteMcp binary not found next to this script (${DIR})" >&2
fi

detect_profile() {
  local shell_name
  shell_name="$(basename "${SHELL:-bash}")"
  case "$shell_name" in
    zsh)  echo "${HOME}/.zshrc" ;;
    bash)
      if [[ -f "${HOME}/.bashrc" ]]; then
        echo "${HOME}/.bashrc"
      else
        echo "${HOME}/.bash_profile"
      fi
      ;;
    *)    echo "${HOME}/.profile" ;;
  esac
}

PROFILE="$(detect_profile)"
touch "$PROFILE"

remove_block() {
  local tmp
  tmp="$(mktemp)"
  # Drop the marker line and the following export PATH line we added.
  awk -v marker="$MARKER" '
    $0 == marker { skip=1; next }
    skip { skip=0; next }
    { print }
  ' "$PROFILE" > "$tmp"
  mv "$tmp" "$PROFILE"
}

if [[ "$REMOVE" -eq 1 ]]; then
  if grep -Fq "$MARKER" "$PROFILE"; then
    remove_block
    echo "Removed SqliteMcp PATH entry from ${PROFILE}"
  else
    echo "Not registered in ${PROFILE}"
  fi
  echo "Restart your shell for the change to take effect everywhere."
  exit 0
fi

if grep -Fq "$MARKER" "$PROFILE"; then
  echo "Already registered in ${PROFILE}"
  exit 0
fi

{
  echo ""
  echo "$MARKER"
  echo "$LINE"
} >> "$PROFILE"

echo "Added ${DIR} to PATH in ${PROFILE}"
echo "Restart your shell or run: source ${PROFILE}"
echo "Then you can run: SqliteMcp"
