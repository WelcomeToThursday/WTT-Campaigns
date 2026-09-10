# Campaign branding

Campaign branding is rendered by UI/Controls/CampaignBranding.cs using the game's Bender font, mint accent lines and a WTT/CAMPAIGNS title. It scales with the UI and does not embed lettering in an image.

The main menu banner and built-in campaign rewards header no longer load the recovered Season 1 PNG or logo video. The second introduction slide uses the clean hub background with this title treatment; legacy hub slides referencing the old artwork receive the same replacement at presentation time.

Existing content IDs and bundle asset paths remain valid. Unused recovered assets may remain in the local bundle, but the UI no longer requests the old lettering.
