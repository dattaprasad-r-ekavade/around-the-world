# Campaign — first-slice design

Decisions that affect code. Not lore.

## Location loads, not open-world cities

The player travels a heightmap wilderness, then **loads** a town or a dungeon. Towns
and dungeons are separate scenes of axis-aligned boxes. This is how Daggerfall swapped
locations. Ember has no terrain streaming for buildings, and a 200 m far plane would
hide a seamless city anyway.

```
Wilderness --enter town--> TownExterior --open door--> Interior
    |                         |
    |                         +--leave--> Wilderness
    +--enter mouth--> Dungeon --exit--> Wilderness
```

## Ground height

`Ember.FirstPersonView.Step` ignores Y from `ResolveWalk`. The eye sits at a constant
standing height, so hills are impossible on that type. Campaign copies the view into
`GroundedView` and samples `groundHeight(x, z)` after XZ resolve:

`eyeY = ground + standingEye + jumpOffset`

Interiors return a constant floor. Wilderness bilinear-samples the heightmap.
Walking on water uses `max(terrain, waterLevel)`.

## Metres

| Quantity | Value | Why |
| --- | --- | --- |
| World | 2048 m square | ~2 km first slice, not a continent |
| Chunk | 64 m | 3×3 visible ≈ 192 m, inside the 200 m far plane |
| Vertex spacing | 2 m | ~1 cm at 256px earth texture is overkill; 2 m is cheap |
| Block | 16 m | Daggerfall-style tiles the generator stitches |
| Far plane | 200 m | engine default; keep it |
| Fog | 140–195 m | hides chunk pop-in |
| Water | 4 m | height threshold, not a fluid |
| Eye | 1.7 m | same as FirstLight |
| Towns | 3 | one hand-built, two generated |
| Dungeon mouths | 5 | one hand-built dungeon, four generated |
| Dungeon rooms | 8–12 | cap so a seed stays walkable |
| Walk | 6 m/s | a kilometre is ~3 minutes |

## Blocks, not unique meshes

Towns and dungeons are prefab rooms (street, plaza, house shell, corridor, chamber)
instanced and translated. Same idea as Daggerfall BLOCK.RMB. Authored as C# lists of
boxes and markers. JSON can wait.

## Billboards

People and trees are camera-facing quads from `SpriteForge`. Facing frames (front /
side / back) are separate drawings, not a rotated mesh. NPC frame is chosen from the
yaw between the figure and the camera.

## What the player does in ten minutes

Spawn on a hill. See fog, trees, and a town marker. Walk to the town (or `goto town 0`).
Press E, load the plaza, talk to the watch, open a shop door, leave. Walk to a cave
mouth, enter a dungeon, swing at the dummy, find the exit, return to wilderness.
Same seed, same map.
