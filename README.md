# PeriHub

Lightweight Linux manager for my peripherals (Logitech PRO X 2, DualSense, ROCCAT Vulcan II, SteelSeries Aerox 5).

- `src/PeriHub.Core` – background process: tray icon (StatusNotifier), battery notifications, update check, Unix-socket API
- `src/PeriHub.Gui` – Avalonia window that talks to the core
- `deploy/install.sh` – builds and installs to `~/.local/share/perihub` with autostart

Requires `headsetcontrol` for headset control and `fwupd` for the update check.
