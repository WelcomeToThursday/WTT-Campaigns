# Local UI build output

Place the generated `seasonalperks_ui.bundle` here, or build it from the companion CJ-SDK project as described in the root README. The bundle and Unity manifests are ignored by Git because they are generated and contain recovered game assets.

Client code can compile without this bundle, but running the seasonal UI and creating a complete release package require it. Perk icons are supplied separately from the local SeasonalPerks asset directory and copied into the server build.
