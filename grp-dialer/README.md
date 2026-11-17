# GRP Dialer

Small Windows 10/11 app that controls a Grandstream GRP2634 desk phone over its
CTI HTTP API: dial, answer, decline, hang up.

## Keys

| Key | Does |
|---|---|
| Numpad Enter (any app) | Brings window up, focuses dialer |
| Enter, number typed | Calls |
| Enter, incoming call | Answers |
| Enter, in a call | Hangs up |
| Esc | Clears dialer |

Main Enter is never hooked. Numpad Enter is only intercepted while another app
has focus; turn that off in Settings.

## Phone setup

1. Phone web UI: **Settings > Call Features > Enable Click-To-Dial = Yes**.
2. Note the phone IP (phone menu: Status > Network).
3. In GRP Dialer, Settings: IP, admin password, account (1 = first line).
   Use **Test**, then **Save**.

Password is stored DPAPI-encrypted in `%APPDATA%\GrpDialer\settings.json`
(readable only by your Windows user).

## API used

```
GET /cgi-bin/api-get_line_status?passcode=PW
GET /cgi-bin/api-make_call?phonenumber=NUM&account=0&passcode=PW
GET /cgi-bin/api-phone_operation?cmd=acceptcall|rejectcall|endcall&passcode=PW
```

Status is polled once per second.

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
