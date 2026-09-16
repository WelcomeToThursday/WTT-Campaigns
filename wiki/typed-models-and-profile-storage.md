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

## Recover characters after deleting a launcher account

Open the WTT-Campaigns link in the launcher to reach the mod home hub, then choose **Character recovery**. This page requires website Administrator access.

1. Close the game and create a replacement account in the launcher.
2. Restart SPT manually so it reloads the launcher accounts, then refresh the recovery page.
3. Choose a surviving character, select the destination account, and confirm recovery.
4. Launch the game with that account and select the recovered campaign character.

Recovery preserves the character save, inventory, and campaign progress, and keeps the destination account's existing characters and selection. It removes stale ownership references from the deleted account and clears the recovered character's stale raid location. Characters whose original account still exists cannot be transferred here. Wiped, retired, or missing character saves cannot be restored by relinking.

Before changing ownership, the server creates verified backups under `SPT_Runtime/user/seasonal/recovery/backups`. If a write is interrupted, return to the page and retry with the same destination account; the saved recovery record prevents accidentally redirecting a partial transfer. Keep that destination account until recovery finishes.

## Older profile locations

The mod moves recognized older campaign profiles into `user/seasonal/profiles` automatically. It backs them up under `user/seasonal/migration-backups` and verifies the copies before removing the old file.

If both locations contain identical copies after an interrupted move, migration can safely retry. If the copies differ, startup stops without overwriting either file.

For a conflict, keep both copies and the migration backups, check the server log for the affected paths, and restore a consistent backup of the linked account and campaign saves. If you need help identifying the correct backup, include the relevant log excerpt in a bug report without sharing credentials or account identifiers.

---

[Documentation home](Home.md) · [Guide navigation](_Sidebar.md)
