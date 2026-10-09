# CouchPilot

Switch your controller on, your game frontend opens. Quit it, your PC goes to
sleep. Nothing else to touch.

Built for couch gaming on a TV, where reaching for a keyboard defeats the point.

## What it does

- **Controller on → a frontend opens.** From a cold desktop or from sleep,
  because switching a controller on is what wakes most PCs anyway.
- **More than one frontend? It asks.** A picker you drive with the pad.
- **Quit the frontend → the PC sleeps.** Only on a deliberate quit, and only if
  a controller is still connected, so closing it at your desk with a keyboard
  leaves the machine alone.
- **Controller off → optional action.** Run something, or sleep.
- **Optional webhooks or MQTT** on launch and exit, for anyone running Home
  Assistant, Hubitat or Node-RED who also wants their TV handled.

Finds **Big Box**, **LaunchBox**, **Playnite** (fullscreen and desktop),
**Steam** and **Steam Big Picture** by itself. Point it at anything else.

## Install

Download from [Releases](../../releases):

- **CouchPilot-Setup-x.y.z.exe** — installer, per user, no admin needed.
- **CouchPilot.exe** — the bare app, if you would rather not install anything.

No .NET prerequisite either way. It lives in the notification area and adds
itself to your startup; turn that off in Settings.

> Windows SmartScreen will warn about an unsigned app from an unknown
> publisher. That is expected for an unsigned binary. Choose *More info* →
> *Run anyway*, or read the source and build it yourself.

## The picker

When more than one frontend is ticked, switching your controller on brings up a
chooser. It is driveable from the pad, because the entire point of this app is
that you have not picked up a keyboard:

| Input | Does |
| --- | --- |
| D-pad or left stick | Move |
| **A** or **Start** | Open it |
| **B** or **Escape** | Cancel, open nothing |
| Wait | Opens the default by itself |

It ignores input for the first moment, so the button that woke the PC does not
instantly choose for you. Touch the d-pad and the countdown stops, so it never
snatches the decision mid-thought. Set the timeout to 0 to wait forever.

## Settings

Right-click the tray icon → **Settings**, or double-click it. The window opens
by itself on first run, or whenever no frontend could be found.

- **Frontends** — tick which to offer, set the default, reorder, add your own,
  and **Open it** to test one immediately.
- **Behaviour** — every toggle and timing value, including the disconnect action.
- **Integrations** — webhooks and MQTT, each with a **Test** button.
- **Controller wake** — see below.
- **Status** — live: controller connected, frontend running, wake armed, and
  which sleep state this machine actually supports.

It is all plain JSON at `%APPDATA%\CouchPilot\config.json` if you prefer, with
**Reload config file** on the tray menu to pick up hand edits.

### Why "watch process" exists

Some launchers are small stubs that start the real binary and exit immediately.
LaunchBox's `BigBox.exe` is one. Watching the stub looks like quitting a second
after opening, so CouchPilot tracks the long-lived process instead. The known
ones are handled for you; set it yourself for anything unusual.

## Controller wake

Waking a sleeping PC with a controller is widely assumed to be purely a
hardware trait. It is not. The receiver must also be **armed** in Windows, and
often is not by default.

**Settings → Controller wake → Set up controller wake** finds receivers that can
wake the PC but are not armed, and arms them with `powercfg -deviceenablewake`.
Windows asks for administrator rights for that one action, which is why the app
itself does not run elevated.

Genuine hardware limits:

- Wired pads usually cannot wake a PC at all.
- An **Xbox Wireless Adapter** or a Bluetooth receiver generally can.
- Some motherboards gate it behind a BIOS option called *USB wake* or *wake on
  USB*. If arming succeeds and nothing happens, look there.

**Modern Standby** machines, reporting `S0 Low Power Idle` rather than
`Standby (S3)`, never fully sleep and behave differently. The Status page tells
you which kind you have.

### Waking is separate from launching

On a normal controller wake the pad goes from absent to present, which is the
trigger. That works because sleeping deliberately re-arms that baseline first.

`Also open if the PC wakes with a pad already on` covers a different case: the
PC resumes for some other reason while a pad happens to be connected. It is
**off** by default, because otherwise waking at your desk with a keyboard would
switch your TV over and open a frontend uninvited.

## How "deliberate quit" is decided

CouchPilot holds a handle on the frontend process and reads its **exit code**. A
clean quit returns 0; a crash or a kill does not. That keeps it frontend
agnostic, with no plugin to install.

Both Big Box and Playnite do expose exact shutdown events
(`BigBoxShutdownBeginning`, `OnApplicationStopped`). Optional plugins using them
may ship later for anyone wanting a more precise signal than an exit code.

## Home Assistant and friends

Two ways, use either or both:

**Webhooks.** Put your webhook URL in Settings → Integrations. CouchPilot POSTs
to it on launch and on exit.

**MQTT.** Broker address, credentials, and a topic and payload for each event.
It connects only to publish rather than holding a session open, since a handful
of messages a day does not justify reconnect handling or a socket kept alive
across sleep.

Neither is required. Leave both blank and CouchPilot never touches the network.

## Troubleshooting

The log is at `%APPDATA%\CouchPilot\couchpilot.log`, reachable from the tray
menu. Every controller transition, launch, exit code, webhook and MQTT publish
is timestamped, because almost everything this app does happens while nobody is
looking at the screen.

| Symptom | Likely cause |
| --- | --- |
| Nothing happens when the pad comes on | It was already on. It fires on the transition, not on presence. Switch it off, wait, switch it on. |
| PC does not sleep on quit | Check the log for the exit code. A non-zero code is treated as a crash by default. |
| Frontend opens at every boot | A pad connected at startup is deliberately ignored. The log records the baseline at start. |
| The picker chose for me | That is the timeout. Raise it, or set it to 0 to wait forever. |
| Quitting one frontend sleeps the PC while another is running | Turn off *Sleep the PC* or raise the minimum run time. |

## Build it yourself

```
dotnet publish CouchPilot.csproj -c Release -o publish
```

Needs the .NET 8 SDK. Output is a single self-contained `publish/CouchPilot.exe`.
CI builds the same thing, plus the installer, on every push.

## Ideas not yet built

- **Sleep from the pad.** Hold Back and Start for a few seconds to sleep without
  going back to the frontend's exit screen.
- **Do not sleep while a game is running**, as a safeguard for unusual setups.
- **Update check** against this repo's releases.
- **Per-frontend plugins** using the exact shutdown events.
- **Steam Big Picture detection** of whether Big Picture specifically is open,
  rather than Steam generally.

## Licence

MIT.
