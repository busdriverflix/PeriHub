#!/usr/bin/env bash
# Needs root once: installs the udev rule so PeriHub can access the mouse without sudo.
set -euo pipefail
[ "$(id -u)" -eq 0 ] || { echo "Run with sudo: sudo $0"; exit 1; }
here="$(cd "$(dirname "$0")" && pwd)"
install -m 644 "$here/70-perihub.rules" /etc/udev/rules.d/70-perihub.rules
udevadm control --reload-rules
# hidraw nodes do not carry idVendor themselves (their parent does), so an attribute filter would skip them: re-trigger all hidraw + usb devices
udevadm trigger --subsystem-match=hidraw --action=change
udevadm trigger --subsystem-match=usb --action=change
echo "Done. If the mouse is still not accessible, unplug and replug its dongle."
