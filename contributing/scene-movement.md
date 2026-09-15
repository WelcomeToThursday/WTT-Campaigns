# Original scenery movement

Movement keeps native components and identities attached to the original object. It does not grant copying support.

The September 15 Interchange log samples add support for trees with `TreeInteractivePart`, vehicles containing standard `Trunk` components, cash-register assemblies containing standard `LootableContainer` drawers, and props containing native loot-point markers/viewers. Select the whole assembly: trunk hinges, drawer endpoints and door-handle animations remain in their existing parent coordinate systems. Linked parts outside the selection and map-trigger interactions reject movement. Props containing interactions retain their original size. Native registered glass transforms refresh after movement and restoration.

Terrain tiles remain restricted because terrain, instanced vegetation and navigation cannot be relocated together by an ordinary prop transform. Occlusion portals refer to baked map visibility and cannot be relocated this way. Assemblies containing doors remain restricted pending adapters for their navigation and audio links. Static batching, unknown components and other existing restrictions still apply.

After manually restarting the game, check moving and rotating a whole vehicle, opening and closing its trunk, moving a cash-register assembly and opening/searching its drawers, and moving a tree. Check undo, layout reset and preview return restore placement and working interactions. Marker movement does not relocate loose loot already spawned elsewhere in the scene. Offline contract checks do not replace these live checks.
