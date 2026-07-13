#!/usr/bin/env bash
# Write predicted Earth (D3) boundary ride mirror blocks for hardware validation.
#
# Usage:
#   ./debug/write-earth-boundary.sh [--dry-run] <127|128|255>
#
# WARNING: This writes only page 0 blocks 5 and 6. It never writes blocks 0, 1, 2, 3, 4, or 7.
# Earth values above 255 are intentionally unsupported: candidate 256/383/384 encodings failed hardware tests.
# Run the read-only preflight from the handoff before using this script.

set -euo pipefail

usage() {
  echo "Usage: $0 [--dry-run] <127|128|255>" >&2
}

dry_run=0
if [[ "${1:-}" == "--dry-run" ]]; then
  dry_run=1
  shift
fi

if [[ $# -ne 1 ]]; then
  usage
  exit 2
fi

rides="$1"
case "$rides" in
  127) hex="18126DEF" ;;
  128) hex="EB129210" ;;
  255) hex="EB12EDE7" ;;
  *)
    echo "Error: unsupported Earth boundary start '$rides'." >&2
    usage
    exit 2
    ;;
esac

commands=$(cat <<EOF
connect
read 1
read 2
read 3
read 4
read 5
read 6
write 5 $hex
write 6 $hex
read 5
read 6
exit
EOF
)

echo "WARNING: writing Earth boundary start $rides as $hex to ride mirror blocks 5 and 6 only."
echo "This script does not write blocks 0, 1, 2, 3, 4, or 7."

if [[ "$dry_run" -eq 1 ]]; then
  echo "Dry run: Pm3Cli commands would be:"
  printf '%s\n' "$commands"
  exit 0
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
printf '%s\n' "$commands" | dotnet run --project "$repo_root/Pm3Cli"
