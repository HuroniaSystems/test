# GRP Dialer

Small Windows 10/11 app that controls a Grandstream GRP2634 desk phone over its
CTI HTTP API: dial, answer, decline, hang up.

## Keys

| Key | Does |
|---|---|
| Numpad Enter (any app) | Brings window up, focuses dialer |
| Enter, phone idle | Calls the typed number |
| Enter, phone in use | Answers if ringing, otherwise hangs up |
| Esc | Clears dialer |

Main Enter is never hooked. Numpad Enter is only intercepted while another app
has focus; turn that off in Settings.

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

In GRP Dialer, Settings: phone IP and its admin or user password. Use
**Test**, then **Save**. Password is stored DPAPI-encrypted in
`%APPDATA%\GrpDialer\settings.json` (readable only by your Windows user).

## API used

All calls are documented in the CTI guide:

```
GET /cgi-bin/api-get_line_status?passcode=PW                 (polled every 1 s)
GET /cgi-bin/api-make_call?passcode=PW&phonenumber=NUMBER
GET /cgi-bin/api-phone_operation?passcode=PW&cmd=acceptcall
GET /cgi-bin/api-phone_operation?passcode=PW&cmd=endcall
GET /cgi-bin/api-phone_operation?passcode=PW&cmd=cancel
```

The guide documents only `"idle"` as a line `state` value, so the app treats
anything else as "in use" and shows the phone's state string as-is. It does
not assume names for ringing/connected states:

- Answer / Enter while in use sends `acceptcall`. The phone replies
  `{"response":"error"}` when there is nothing to accept.
- Enter then falls back to `endcall`, then `cancel` ("reject or cancel calls").
- The window pops to front when the line leaves idle without this app having
  dialed in the last 10 seconds.

## Build

Requires .NET 8 SDK on Windows.

```
dotnet publish src/GrpDialer/GrpDialer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

CI (`.github/workflows/grp-dialer.yml`) builds `GrpDialer.exe` on every push
and uploads it as the `GrpDialer-win-x64` artifact.

## Style

Colors mirror the Huronia admin panel tokens (`admin/src/styles/tokens.css` in
the Website repo). No app icon.
