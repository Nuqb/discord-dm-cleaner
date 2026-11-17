# v1.0.2

The cleaner now sends one fewer request per deleted message. The scan checks
the author, channel, and message type. After confirmation, the cleaner rechecks
the account and recipient once, then deletes each eligible message by its scanned ID.

- Long conversations use less memory during scanning. Duplicate tracking is limited to the current page.
- A missing or malformed message type stops the scan before deletion can begin.
- The account-risk warning appears beside the token field and in the setup guide.
- Stopping and scanning again in the same window preserves Discord's cooldown.

Download `Discord-DM-Cleaner-v1.0.2-Windows.zip`, extract it, and open
`DiscordDmCleaner.exe`. The ZIP checksum is in `SHA256SUMS.txt`.

Requires Windows with .NET Framework 4.x. The executable is unsigned.
Deletion is permanent. Discord prohibits personal account automation and may
terminate accounts for it. Tests use simulated Discord responses.
