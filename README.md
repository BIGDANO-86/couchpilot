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

Right-click the tray icon → **Settings**, or double-click the icon. The window
opens by itself the first time, and any time no frontend could be detected.

It shows live status while you work: whether a controller is connected right
now, whether the frontend is running, whether your controller is armed to wake
the PC, and which sleep state your machine actually supports. There are **Try
it** buttons for launching and sleeping, and a **Test** button on each webhook,
so you can confirm everything without waiting for the real thing to happen.

Everything is still plain JSON at `%APPDATA%\CouchPilot\config.json` if you
prefer, with **Reload config file** on the tray menu to pick up hand edits.

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
| `ShowNotifications` | `true` | Brief tray notification when something happens. |
| `LaunchOnWakeWithController` | `false` | Also launch if the PC resumes with a pad already on. See below. |

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

## Controller wake

Waking a sleeping PC with a controller is widely assumed to be purely a
hardware trait. It is not. The device must also be **armed** in Windows, and it
often is not by default.

CouchPilot reports the state and can fix it: **Settings → Set up controller
wake**. It finds receivers that are capable of waking the PC but not armed, and
arms them with `powercfg -deviceenablewake`. Windows asks for administrator
rights for that one action, which is why the app itself does not run elevated.

Things that genuinely are hardware limits:

- Wired pads usually cannot wake a PC at all.
- An **Xbox Wireless Adapter** or a Bluetooth receiver generally can.
- Some motherboards gate it behind a BIOS option, often called *USB wake* or
  *wake on USB*. If arming succeeds but nothing happens, check there.

**Modern Standby** machines, which report `S0 Low Power Idle` rather than
`Standby (S3)`, never fully sleep and behave differently. The settings window
tells you which kind you have.

### Why waking is a separate thing from launching

On a normal controller wake, the pad goes from absent to present, which is what
triggers a launch. That works because sleeping deliberately re-arms that
baseline first.

`LaunchOnWakeWithController` covers a different case: the PC resumes for some
other reason while a pad happens to already be connected. It is **off** by
default on purpose, because otherwise waking the machine with a keyboard at
your desk would switch your TV over and open your frontend uninvited.

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

## Ideas not yet built

Open to opinions on which of these are actually worth having:

- **Sleep from the pad.** Hold a button combination, say Back and Start for
  three seconds, to sleep without reaching for the frontend's exit. Removes the
  last reason to touch a keyboard.
- **Do not sleep while a game is running.** Big Box and Playnite minimise
  behind a game rather than exiting, so this is only a safeguard for odd setups.
- **Several frontends.** Pick which one to open, or offer a chooser when a pad
  connects.
- **Update check** against this repo's releases, with a tray prompt.
- **Launch something when the pad disconnects**, for people who want the
  opposite behaviour.
- **MQTT** as an alternative to webhooks, for Home Assistant users who prefer it.
- **Per-frontend plugins** using the exact shutdown events
  (`BigBoxShutdownBeginning`, `OnApplicationStopped`) for anyone who wants a
  signal more precise than an exit code.

## Build it yourself

```
dotnet publish CouchPilot.csproj -c Release -o publish
```

Needs the .NET 8 SDK. Output is a single self-contained `publish/CouchPilot.exe`.

## Licence

MIT.
