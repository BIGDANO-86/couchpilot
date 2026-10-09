# CouchPilot

Switch your controller on, your game frontend opens. Quit the frontend, your PC
goes to sleep. Nothing else to touch.

Built for couch gaming on a TV, where reaching for a keyboard defeats the point.

## What it does

- **Controller on → frontend opens.** Works from a cold desktop or from sleep,
  because switching a controller on is what wakes most PCs anyway.
- **Quit the frontend → PC sleeps.** Only on a deliberate quit, and only if a
  controller is still connected, so closing it at your desk with a keyboard
  leaves the machine alone.
- **Optional webhooks** on launch and on exit, for anyone running Home
  Assistant, Hubitat, Node-RED or similar who also wants their TV handled.

Works with **LaunchBox / Big Box**, **Playnite**, and in principle any frontend
you point it at, including Steam Big Picture and Kodi.

## Install

1. Download `CouchPilot.exe` from [Releases](../../releases).
2. Run it. It lives in the notification area.
3. Right-click the tray icon → **Edit settings** if you need to change anything.

No installer, no .NET prerequisite, no admin rights. It adds itself to your
user's startup so it is there after a reboot; turn that off in the settings.

> Windows SmartScreen will warn about an unsigned app from an unknown
> publisher. That is expected for an unsigned binary. Choose *More info* →
> *Run anyway*, or check the source and build it yourself.

## Settings

Settings live in `%APPDATA%\CouchPilot\config.json`. Edit, then
right-click the tray icon → **Reload settings**.

| Setting | Default | What it does |
| --- | --- | --- |
| `FrontendPath` | auto-detected | The frontend to launch. |
| `WatchProcessName` | derived | The long-lived process to watch, without `.exe`. See the note below. |
| `LaunchOnControllerConnect` | `true` | Launch when a controller is switched on. |
| `SleepOnFrontendExit` | `true` | Sleep the PC when the frontend quits. |
| `RequireCleanExit` | `true` | Only sleep on exit code 0, so a crash does not sleep the PC. |
| `RequireControllerForSleep` | `true` | Only sleep if a controller is still connected. |
| `MinimumRunSeconds` | `30` | Ignore an exit sooner than this, to dodge crash loops. |
| `SleepDelaySeconds` | `6` | Pause before sleeping, so shutdown screens and webhooks finish. |
| `ControllerPollSeconds` | `2` | How often to check for controllers. |
| `CooldownSeconds` | `45` | Ignore repeat triggers inside this window. |
| `WebhookOnLaunch` | empty | POSTed when a launch is triggered. |
| `WebhookOnExit` | empty | POSTed when the frontend quits. |
| `StartWithWindows` | `true` | Adds a `Run` key entry for your user. |

### Why `WatchProcessName` exists

LaunchBox ships a small `BigBox.exe` stub in its root folder that starts the
real binary and can exit straight away. Watching the stub looks like you quit
immediately. CouchPilot tracks the long-lived process instead. Big Box and
Playnite are handled automatically; set this yourself for anything unusual.

## How "deliberate quit" is decided

CouchPilot holds a handle on the frontend process and reads its **exit code**.
A clean quit returns 0; a crash or a kill does not. That keeps it frontend
agnostic, with no plugin to install.

Both Big Box and Playnite do expose an exact shutdown event
(`BigBoxShutdownBeginning` and `OnApplicationStopped`), and optional plugins
using them may ship later for people who want the precise signal. The exit code
covers it without one.

## Troubleshooting

The log is at `%APPDATA%\CouchPilot\couchpilot.log`, reachable from the tray
menu. Every controller transition, launch, exit code and webhook result is
recorded with a timestamp, because almost everything this app does happens while
nobody is looking at the screen.

| Symptom | Likely cause |
| --- | --- |
| Nothing happens when the controller comes on | It was already on. It fires on the transition, not on presence. Switch it off, wait, switch it on. |
| PC does not sleep on quit | Check the log for the exit code. A non-zero code is treated as a crash by default. |
| Frontend opens at every boot | A controller connected at startup is deliberately ignored; if it still happens, check the log for the baseline line. |

## Build it yourself

```
dotnet publish CouchPilot.csproj -c Release -o publish
```

Needs the .NET 8 SDK. Output is a single self-contained `publish/CouchPilot.exe`.

## Licence

MIT.
