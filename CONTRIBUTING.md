# Contributing

Bug reports, documentation improvements, and pull requests are welcome.

## Report a problem

[Open an issue](https://github.com/Nuqb/discord-dm-cleaner/issues) with:

- Your Windows and app versions.
- What you expected, what happened, and steps to reproduce it.
- The error message or a screenshot with private details removed.

Never include account tokens, passwords, real message contents, or private
account/channel IDs. Use fake data in examples and tests. If a token was
exposed, remove it and secure your account rather than reposting it.

## Make a change

1. Fork the repository and create a branch for one focused change.
2. Follow the existing C# style and keep cleanup checks easy to review.
3. On Windows, run:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Test
   ```

4. Open a pull request describing the change and how you verified it.
   For interface changes, include a screenshot using fake data.

The tests use simulated responses; no real Discord account is needed. Add or
update regression coverage when changing cleanup behavior. Keep generated
binaries, local credentials, and build output out of commits.

## Cleanup behavior

Changes must preserve account and recipient checks, one-to-one DM restrictions,
exact message ownership verification, explicit deletion confirmation,
cancellation, and rate-limit handling. Do not add token collection from saved
credentials, credential persistence, or private message logging. Discuss large
changes in an issue first.

## License

Contributions are provided under the repository's [MIT license](LICENSE).
See [NOTICE](NOTICE) for third-party branding information.
