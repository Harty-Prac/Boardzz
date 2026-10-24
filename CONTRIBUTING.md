# Contributing to Boardzz

Thanks for wanting to help! Anyone is welcome to contribute.

## Ways to help

- **Report a bug**: open an issue with steps to reproduce, what you expected, and your Windows version.
- **Suggest a feature**: open an issue describing the idea and why it's useful.
- **Send code**: fork the repo, make a branch, and open a pull request.

## Pull requests

1. Fork the repository and create a branch: `git checkout -b feature/my-idea`
2. Build and test locally with `dotnet run`
3. Keep the build free of warnings
4. Open a pull request explaining what changed and why

## Code style

- **camelCase** for all names (types, methods, fields, locals) unless the framework forces otherwise (e.g. `Dispose`, `OnRender`)
- Small, focused files: audio in `audio/`, UI controls in `ui/`
- No network code, telemetry, keyboard hooks or packers (keeps Boardzz clean on antivirus scans)
- Fix root causes rather than suppressing warnings

## Questions

Open an issue — happy to help you get started.
