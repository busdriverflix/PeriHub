#!/usr/bin/env bash
set -euo pipefail
rm -f "$HOME/.config/autostart/perihub-core.desktop" "$HOME/.local/share/applications/perihub.desktop"
rm -rf "$HOME/.local/share/perihub"
echo "PeriHub removed."
