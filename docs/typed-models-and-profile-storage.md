# Profile backups and recovery

Campaign characters use separate save files linked to your launcher account. Keep these files together when backing up or moving an installation.

| Location under your SPT installation | Contents |
| --- | --- |
| `SPT_Runtime/user/profiles` | Regular launcher-account profiles |
| `SPT_Runtime/user/seasonal/profiles` | Campaign PMC and Scav profiles |
| `SPT_Runtime/user/profileData` | Account-link data |
| `SPT_Runtime/user/mods/WTT-Campaigns/creator` | Drafts, published campaign packs, artwork and campaign usage records |

## Back up and update

1. Close the game and server before making a consistent backup or replacing files.
2. Back up the profile locations above, your WTT-Campaigns configuration and the entire server mod's `creator` folder.
3. Install the complete matching release as described in the [installation guide](../README.md#installation).
4. Keep the campaign packs used by existing characters.

Do not remove linked campaign profiles manually. Use the character selector's confirmed [Delete or Wipe actions](characters.md#delete-or-wipe) when you intend to remove a character.

## Older profile locations

The mod moves recognized older campaign profiles into `user/seasonal/profiles` automatically. It backs them up under `user/seasonal/migration-backups` and verifies the copies before removing the old file.

If both locations contain identical copies after an interrupted move, migration can safely retry. If the copies differ, startup stops without overwriting either file.

For a conflict, keep both copies and the migration backups, check the server log for the affected paths, and restore a consistent backup of the linked account and campaign saves. If you need help identifying the correct backup, include the relevant log excerpt in a bug report without sharing credentials or account identifiers.
