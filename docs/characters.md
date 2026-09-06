# Seasonal character selection

The selector supports multiple seasonal characters, including multiple characters in the same season. Each card has a stable character ID. The permanent creation card and footer shortcut open a season picker before the native faction/appearance and modifier screens. Cards retain the recovered artwork, font, tint and hover presentation.

Mouse wheel, horizontal wheel, dragging, arrow buttons and Left/Right keys browse a wrapping cylindrical carousel. Cards scale and fade with distance from its center; hidden equipment previews are released. Navigation is blocked during dialogs and requests.

## Delete and wipe

Seasonal cards provide separate confirmations naming the character and season. Cancel and Escape do not submit an operation. The regular card represents the launcher account and is not deletable through this screen.

Delete removes the seasonal character and its saved profile. Wipe follows the supplied live Seasonal Character Wipe reference: items, equipment, currency, quests, skills, modifiers and all other seasonal progression are removed, while earned in-game achievements are preserved. The client returns to faction/appearance creation and the player chooses faction, head, voice and modifiers again. Recreation uses the account's current game edition for the native starting profile, then applies the selected season's configured starting grants. Other characters are unaffected.

A wiped character keeps its card and season. Cancelling creation leaves a **RECREATE** action for later. Its achievement receipt is saved in the root account link before the old profile file is removed, so achievements survive cancellation and server restarts. The receipt is cleared only after successful recreation. Creating/recreating uses an operation ID and choice fingerprint to prevent duplicate submissions. Replaying the completed wipe request cannot wipe its newly recreated character again.

The client flushes pending operations and reconnects to the regular character before wiping/deleting a currently loaded seasonal character. The server independently checks ownership, raid state and active identity. The root account is rejected by both destructive endpoints.

## Several seasons in one server session

All compatible published seasons are loaded at startup. The server's selected pack remains the default; characters can select any playable season without restarting between them. New or updated packs still require a server restart for content registration. For each season, the highest compatible revision is chosen, except that the configured default pack retains its selected revision. Invalid packs are omitted, and missing seasons leave their existing cards unavailable for play.

Effects, starting grants, document loot, reward progress, exchanges and trader unlocks resolve from the character's season. Imported quest visibility and acceptance are restricted to that season. Existing current and archived account links migrate to the character list without replacing profiles or merging their progression.

## Verification

`tools/test_characters.py` creates synthetic accounts in `Testing/Server` and covers same-season siblings, different seasons, retry handling, ownership, native raids, quest isolation, reward claims, deletion and achievement-preserving wipes/recreation. It expects the published fixture from `Tests --creator-fixture` alongside the built-in season. `tools/test_character_restart.py prepare|verify` exercises the old account-link format across a restart. `tools/test_wipe_restart.py prepare|verify` verifies postponed recreation and achievement persistence across a restart. All fixture operations refuse installed player profiles.

`tools/unity/SeasonalCharactersPreview.cs` runs against the synchronized UI sources in CJ-SDK. It renders the carousel, season picker, delete/wipe confirmations and wiped card at 1920×1080, 1280×720 and 2560×1080, with interaction checks for target identity, cancellation, wrapping and recreation. Editor equipment images are stand-ins; installed-game animation, model lighting and reconnect behavior still require an in-game check.

Build client and server together. The carousel uses the existing artwork bundle; no new recovered media is required. `tools/package_ui.ps1` stages the update without installing it into the running game/server.
