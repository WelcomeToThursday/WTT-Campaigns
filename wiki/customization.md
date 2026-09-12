# Character customization

Open **Character → Customization**, between Health and Skills, outside a raid. Choose a face or voice; changes save automatically. The speaker button previews the selected voice. Leaving the tab or closing Character waits for the final save.

The screen reuses EFT's profile-creation appearance view: all eight native heads for each PMC faction, rendered portrait cards, the rotating head preview, faction artwork, and the voice selector and audio banks. The creation title, nickname field, faction selection, and creation buttons are hidden. The grid expands to three columns and the voice selector sits at the bottom. No generated or placeholder portraits, logos, voices, or icons are used.

Head and voice save together to the authenticated active character through the normal SPT save pipeline, including seasonal profile routing. Invalid or wrong-faction choices are rejected before mutation. Failed saves restore the confirmed appearance and card selection. Clothing, equipment, nickname, faction, and other profile data are preserved. The tab is disabled during raids, for Scavs, and when inventory editing is blocked.

The only additional image is the live `Face_Icon`, embedded in the client assembly. Its extraction source and SHA-256 are recorded in `Client/Customization/Assets/provenance.json`; the character portraits and voices remain native game assets.

Validation uses offline profile-save contracts, game assembly compatibility checks, authentic asset checks, and file-only deployment verification. It does not start any game, server, or test runtime. Both the client and server assemblies are required; manually restart both after installation to load them. In-game visual and interaction verification remains a manual check.
