#!/usr/bin/env bash
# Builds PeriHub, installs it to ~/.local/share/perihub and starts the core automatically at login.
# Undo with: deploy/uninstall.sh
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
dest="$HOME/.local/share/perihub"

dotnet publish "$root/src/PeriHub.Core" -c Release -o "$dest" --nologo -v q
dotnet publish "$root/src/PeriHub.Gui"  -c Release -o "$dest" --nologo -v q

mkdir -p "$HOME/.config/autostart"
cat > "$HOME/.config/autostart/perihub-core.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=PeriHub Core
Comment=Peripheral manager (tray icon, battery warnings)
Exec=$dest/PeriHub.Core
Terminal=false
X-GNOME-Autostart-enabled=true
DESKTOP

cat > "$HOME/.local/share/applications/perihub.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=PeriHub
Exec=$dest/PeriHub.Gui
Terminal=false
Categories=Utility;
DESKTOP

echo "Installed to $dest. The core starts at your next login; to start it now run: $dest/PeriHub.Core &"
