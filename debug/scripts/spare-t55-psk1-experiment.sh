#!/usr/bin/env bash
# Spare T55: temporarily program PSK1 block0 for modulation-detection experiments.
# WARNING: Block 0 is the configuration block — only use on a spare tag.
#
# Usage:
#   ./debug/scripts/spare-t55-psk1-experiment.sh backup
#   ./debug/scripts/spare-t55-psk1-experiment.sh program-psk1
#   ./debug/scripts/spare-t55-psk1-experiment.sh restore-ask
#   ./debug/scripts/spare-t55-psk1-experiment.sh verify-native
#
# Requires: pm3 on PATH, tag on reader, PORT default /dev/cu.usbmodem1201

set -euo pipefail
PORT="${PM3_DEVICE_PORT:-/dev/cu.usbmodem1201}"
ASK_BLOCK0="00148040"
PSK1_BLOCK0="00141040"
BACKUP_JSON="$(dirname "$0")/../tag-backups/spare-t55-ask-backup.json"

pm3c() { pm3 -p "$PORT" -c "$1"; }

cmd="${1:-}"
case "$cmd" in
  backup)
    pm3c "lf t55 detect; lf t55 read -b 0; lf t55 read -b 5; lf t55 read -b 6"
    echo "See also: $BACKUP_JSON"
    ;;
  program-psk1)
    echo "Programming block0 ASK $ASK_BLOCK0 -> PSK1 $PSK1_BLOCK0"
    pm3c "lf t55 detect; lf t55 write -b 0 -d $PSK1_BLOCK0 --verify"
    pm3c "lf t55xx config -c $PSK1_BLOCK0; lf t55 detect; lf t55 read -b 0"
    echo "pm3 should show PSK1. Native executor may still report 'No T55xx chip detected' (ASK-only demod)."
    ;;
  restore-ask)
    echo "Restoring block0 to ASK $ASK_BLOCK0"
    pm3c "lf t55xx config -c $PSK1_BLOCK0; lf t55 write -b 0 -d $ASK_BLOCK0 --verify"
    pm3c "lf t55 detect; lf t55 read -b 0; lf t55 read -b 5"
    ;;
  verify-native)
    PM3_DEVICE_PORT="$PORT" dotnet run --project "$(dirname "$0")/../Psk1TagProbe"
    ;;
  *)
    echo "Usage: $0 {backup|program-psk1|restore-ask|verify-native}"
    exit 1
    ;;
esac
