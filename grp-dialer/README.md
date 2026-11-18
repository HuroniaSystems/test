# GRP Dialer

Small Windows 10/11 app that controls a Grandstream GRP2634 desk phone over its
CTI HTTP API: dial, answer, hang up, and queue the next calls.

## Using it

GRP Dialer lives in the notification area (tray) by the clock, like OTC Monitor.
Click its green phone icon to open the dialer just above the taskbar; it hides
again when you click another app, press **Esc** on an empty box, or click ×.
There is no taskbar button. To quit, right-click the tray icon > **Exit GRP
Dialer** (or Settings > Advanced and help > Quit). On Windows 11 a new tray icon
may start under the **^** arrow; drag it onto the taskbar to keep it visible.

Type a number, press **Enter**. That's it. The box formats the number as you
type, e.g. `(705) 555-1234`, and ignores letters; extensions and `*`/`#` codes
stay as typed.

### Incoming call bar

While a call rings in, a thin strip the same width as the dialer sits above the
clock (or just above the dialer when it's open): a green phone, the caller's
number, **Incoming Call** (plus any PropelRx match), a gently glowing
**Answer** and **Decline**. It stays while ringing, even when you click away,
and never takes keyboard focus. Click the number to open the dialer.

When the call is answered (here or on the handset) the phone turns orange, the
strip fades and the dialer opens without taking focus. If you don't touch it,
it tucks away again when the call ends.

### During a call

The state badge sits on the caller's line with the call time: **Connected
1:23**, **On hold 0:42**. The small square button beside **Hang up** puts the
call on hold (pause icon) and resumes it (play icon). The key hint under the
buttons always says what Enter does right now: **[ENTER] Call**, **Answer**,
**Hang up** or **Add to queue**.

Hold uses `api-phone_operation?cmd=holdcall`. The CTI guide has no resume
command, so resume presses the phone's HOLD key (`api-send_key?keys=HOLD`),
falling back to the line key (`keys=LINE1`).

### When the phone can't be reached

The header shows a short reason (**Failed to connect**, **Wrong password**,
**Remote control off**, **Not set up**; hover for details) and the dialer is
dimmed and locked until the phone answers again. Settings stays reachable.

### Caller lookup (PropelRx)

For calls that weren't dialed from GRP Dialer, the caller's number is looked up
in PropelRx, the same read-only web API OTC Monitor uses. A small muted caption
appears under the number on the call bar and in the dialer:

- one patient with that phone: **Potentially JANE DOE**
- several: **Multiple linked profiles**
- none (or PropelRx unreachable): nothing

Set it up in Settings > **Caller lookup**: enter the PropelRx server (e.g.
`svr1`, or a full URL) and use **Test lookup**. Requests sign in as the current
Windows user, like a browser on the network; if PropelRx refuses, set a Windows
account under Advanced (password stored with DPAPI). Only 10-digit North
American numbers are looked up. Results are kept in memory for the current call
and never written to disk.

```
GET http://<server>/PFSP/HTML/api/v1/SearchPatient?LastName=&FirstName=&ProvincialHealthNumber=&PhoneNumber=7055551234&GroupName=&Address=&BirthDate=&_=
```

The plain digits are tried first, then `(705) 555-1234` and `705-555-1234`.
Records whose phone matches are used; if none show the number but PropelRx
returned three or fewer, those are trusted (it matched another phone field).

| Key | Phone idle | On a call |
|---|---|---|
| Enter, number typed | Calls it | Adds it to the queue |
| Enter, box empty | Calls the next queued number | Answers if ringing, otherwise hangs up |
| Esc | Clears the box; hides the dialer if already empty | Same |
| Numpad Enter (any app) | Brings the dialer up | Brings the dialer up |

Main Enter is never hooked. Numpad Enter is only intercepted while another app
has focus; turn that off in Settings.

### Call queue

While on a call, keep typing numbers and pressing Enter. They line up under
**Up next**. When the call ends, the next number is dialed after a short
countdown (3 s, set in Settings > Advanced).

- **Pause** stops the queue; **Resume** starts it again. **Clear** empties it; × removes one number.
- The queue waits while a number is typed in the box, while Settings is open, and while the phone is offline.
- If the dialer is hidden when the countdown starts, it pops up (without taking focus) so you can see it and pause.
- An incoming call during the countdown cancels it; the queue continues after that call.
- If the phone refuses a queued number, it goes back to the top and the queue pauses.
- The queue lives in memory only: closing the app empties it, so nothing dials by surprise on the next start.

## Phone setup

From Grandstream's *GRP26xx Series - CTI Guide*:

1. Phone web UI as admin: **Network > Remote Control**
   (GRP260x: Network Settings > Advanced Settings > Remote Control).
2. **Action URI Support**: Enabled.
3. **Action URI Allowed IP List**: this PC's IP, or `any`.
4. **Remote Control Pop-up Window Support**: Disabled (else the phone asks for
   confirmation on every command).
5. Save and Apply.
6. GRP260x only: enable **Click-To-Dial** under Phone Settings > Call Settings.

Supported on GRP2634 firmware 1.0.9.22+.

In GRP Dialer, Settings: phone address and its admin or user password. Use
**Test connection**, then **Save**. The password is stored DPAPI-encrypted in
`%APPDATA%\GrpDialer\settings.json` (readable only by your Windows user).
Unexpected errors are written to `error.log` in the same folder; the app keeps running.

## API used

All calls are documented in the CTI guide:

```
GET /cgi-bin/api-get_line_status?passcode=PW                 (polled every 1 s; 3 s while offline)
GET /cgi-bin/api-make_call?passcode=PW&phonenumber=NUMBER
GET /cgi-bin/api-phone_operation?passcode=PW&cmd=acceptcall
GET /cgi-bin/api-phone_operation?passcode=PW&cmd=endcall
GET /cgi-bin/api-phone_operation?passcode=PW&cmd=cancel
```

The guide documents only `"idle"` as a line `state` value. Other state strings
are matched loosely to plain labels (Incoming call, Calling, Connected, On hold);
anything unrecognised shows as "On a call". The raw value is in the status
pill's tooltip.

- Answer / Enter while in use sends `acceptcall`. The phone replies
  `{"response":"error"}` when there is nothing to accept.
- Enter then falls back to `endcall`, then `cancel` ("reject or cancel calls").
- The window pops to front when the line leaves idle without this app having
  dialed in the last 10 seconds.
- One failed poll is ignored; the phone shows as offline after two in a row.

## Build

Requires .NET 8 SDK on Windows.

```
dotnet test tests/GrpDialer.Tests
dotnet publish src/GrpDialer/GrpDialer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

CI (`.github/workflows/grp-dialer.yml`) runs the tests and builds `GrpDialer.exe`
on every push, uploading it as the `GrpDialer-win-x64` artifact. Run the
workflow manually with a `release_tag` (e.g. `grp-dialer-v1.1.0`) to publish a
GitHub release.

## Style

Light theme only. Palette, Roboto font and control shapes match the OTC Monitor
client (brand green `#016E4F` on `#F3F6F8`, white cards). Icons are one solid
set from Material Design Icons (Pictogrammers, Apache 2.0), drawn from
`Icons.cs`; animations are short and eased (`Motion.cs`). Roboto is bundled
under the Apache 2.0 license (`src/GrpDialer/Fonts/LICENSE`).
