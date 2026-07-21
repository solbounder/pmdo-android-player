# Security policy

## Supported versions

Only the latest preview release is supported. This project is experimental and
does not provide a production security guarantee.

## Reporting a vulnerability

Please do not open a public issue for a vulnerability that could expose user
files, bypass archive limits, execute unexpected code, or compromise imported
runtime and save data.

Use GitHub private vulnerability reporting from the Security tab of this
repository. Include the affected version, Android version and device, steps to
reproduce, expected behavior, actual behavior, and the copyable engine error
report when available.

Ordinary crashes, mod compatibility problems, controller issues, and display
problems may be reported through public GitHub Issues.

Imported Lua mods are trusted executable code and are not sandboxed. A report
that an intentionally malicious imported mod can execute within the app is not
by itself a vulnerability unless it escapes the documented app boundary or
accesses data Android has not granted to the app.
