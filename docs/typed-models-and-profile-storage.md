# Typed models and seasonal profile storage

Quest templates, objectives, rewards, trader offers, imported items, progression payloads, hub mutations, pending operations and media manifests use concrete models and typed collections. Shared native contracts live in `Shared/Native`; the server and authoring UI read and edit their properties directly. Native scalar-or-list targets and indexed-or-grid item locations have explicit union contracts.

JSON containers are confined to the shared serialization implementation: preserving unknown imported fields and original wire shapes, and canonicalizing gameplay hashes. They are not exposed by gameplay or editor models. Imported numeric strings, absent fields, explicit nulls and numeric quest statuses remain unchanged until edited, preserving existing packs and their gameplay hashes. New supported fields should be added to the concrete contracts rather than accessed through extension data.

Launcher accounts remain in `SPT_Runtime/user/profiles`. Seasonal PMC/Scav character files live in `SPT_Runtime/user/seasonal/profiles`. Their identifiers and account links remain unchanged. The launcher account list excludes these character identities even though the server loads them for play.

On startup the mod discovers character ownership from current and legacy account links, with persisted ownership as a recovery fallback. Existing seasonal files are copied to the new folder and backed up under `user/seasonal/migration-backups`. SHA-256 checks verify both copies before the old file is removed. Identical copies from an interrupted migration are safe to retry. Differing copies stop startup for recovery without overwriting either file.

Storage hooks are enabled before SPT loads profiles. Migration and loading complete before SPT's backup service starts. Saving, deletion, wiping, startup/periodic backups and corrupt-profile recovery retain SPT's native behavior while resolving seasonal paths to the separate folder. Account-link data stays in `user/profileData`.

Validation:

- `dotnet run --project Tests -c Release` covers typed imports, exact captured-data round trips, editing, copying, authoring operations and gameplay identity.
- `tools/test_profile_storage.ps1` stages a dedicated disposable server at `Testing/TypedProfilesServer` on port 6988. It creates synthetic accounts and checks launcher filtering, native routes, restart recovery, migration, interrupted/conflicting copies, native backup restoration, wiping, recreation and deletion. It never copies installed profiles or controls other servers.

Install the matching server, shared and client assemblies together. Restart the server for migration and start a fresh game process to load the updated client.
