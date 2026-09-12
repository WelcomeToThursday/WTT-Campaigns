# Salvage quest zones

Install matching WTT-CommonLib client and server components (3.0.6 or later). Campaigns declares both as required dependencies; CommonLib is installed separately.

In the quest editor, add a **Salvage** objective to a story quest, or add it inside a counter. Capture its zone in a connected raid, then open **Zones and captures**, enable **Salvage**, and select the required item, duration, whether to consume the item, and optional rewards. Rewards can go to ordinary inventory or quest inventory (use the latter for quest items). A completed salvage satisfies the objective once; leave its quantity at 1. The zone's duration controls the interaction.

You can also enter an existing CommonLib salvage zone ID in the objective's zone ID field. The mod that owns that zone remains responsible for loading and configuring it.

Captured drafts can be saved before selecting items. Publishing requires a valid required item, positive duration and valid reward counts. Salvage and item placement use separate zones because placement takes priority over the salvage interaction. Campaign copies repair salvage references to their copied zones. Campaign salvage zones are loaded only for the active campaign character on their authored map and scene.

Publish the campaign and restart SPT to load the updated pack. Runtime behavior uses CommonLib's salvage trigger, item handling and condition progress tracking.
