# Multisim Database Retry

A small Windows launcher for intermittent Multisim database-access errors at startup. It starts one empty instance at a time, retries recognized database-error dialogs, and leaves the first instance whose main window remains stable for eight seconds.

**This is a workaround, not a database repair.** A successful startup check does not verify every database or simulation feature. NI documents a fix for one Chinese-locale Windows 11 database issue in [Circuit Design Suite 14.3 Patch 3](https://www.ni.com/en/support/documentation/supplemental/26/circuit-design-suite-14-3-patch-3-details.html); prefer that official fix when applicable.

## Run

1. On Windows, run `build.cmd` to compile `dist\MultisimRetryLauncher.exe` using the installed .NET Framework C# compiler.
2. Run `启动Multisim.cmd` (or launch the executable directly). The default path targets Multisim 14.3 in the standard Program Files (x86) directory. Use `dist\MultisimRetryLauncher.exe --exe "D:\Your Installation\multisim.exe"` for another location.
3. Wait for the launcher to report success before opening or editing a circuit.

The launcher refuses to start when a same-name process is already running. It force-terminates only failed instances it started itself, and preserves unknown dialogs for inspection. Defaults: 10 attempts, 75-second startup timeout, 8-second stability check, and 2 seconds between attempts. Press `Ctrl+C` to stop retrying and leave the current instance open.

If Multisim has an existing Windows compatibility setting requiring administrator rights, the launcher requests matching elevation. The launcher itself does not change system locale or directly modify NI binaries or databases; Multisim may still read or write its own data while starting.

## Tests and privacy

Run `tests\run-tests.cmd` on an interactive Windows desktop. It builds a fake WinForms app and checks the retry, failure, unknown-dialog, splash-screen, timeout and existing-process cases; it does not start real Multisim. The test runner applies PowerShell's execution policy override only to the test process.

`dist/` and logs are ignored by Git. Logs can contain local paths and dialog text; review them before sharing. One previous real-machine test passed the startup check on the second attempt after the first database error. Results can vary by machine and issue.

No NI executables, databases, licenses or assets are distributed here. This independent project is not affiliated with or endorsed by NI. Source code: [MIT License](LICENSE). See the Chinese [README](README.md) for details.
