# Model catalogue and BattleMech animation

The Mechs page assembles a chassis from torso, head, arms and legs. An animation
selects the appropriate shapes for those parts; one bank entry is not one whole
Mech. Several animations may reuse the same shape.

Supported sequences include standing, walking, jumping where the chassis can
jump, falling and recovery transitions. The viewer preserves the known native
frame relationships. It does not invent a modern skeleton or treat every six
consecutive resources as another complete pose.

## Finding a shape

| Chassis | Model bank |
| --- | --- |
| Locust | LOCPCK.TBL |
| Jenner | JENPCK.TBL |
| Phoenix Hawk | PHAPCK.TBL |
| Shadow Hawk | SHAPCK.TBL |
| Rifleman | RIFPCK.TBL |
| Warhammer | HAMPCK.TBL |
| Marauder | MARPCK.TBL |
| BattleMaster | BMAPCK.TBL |

Select a bank on Models, then choose the resource. The information pane lists
its body part and known animation/frame uses. All 780 shipped Mech shapes have
recovered membership. A shape shared by several sequences keeps all those uses
in the catalogue rather than being assigned just one guessed animation.

For CLI inspection:

```powershell
dotnet $cli models list --bank 'D:/Games/MechWarrior/locpck.tbl' --game-dir 'D:/Games/MechWarrior'
dotnet $cli models inspect --bank 'D:/Games/MechWarrior/locpck.tbl' --resource 0 --game-dir 'D:/Games/MechWarrior'
```

Use the reviewed executable alongside model banks for chassis/animation
definitions. Without those definitions, unidentified geometry stays unidentified.

## Terrain, buildings and effects

TERPCK contains the labelled hills and rivers. OTHPCK includes shells, missiles,
explosions, smoke and sparks. The objective-building shortcut selects the
yellow/black building used by the game. User-supplied visual labels are kept
distinct from identities recovered from game consumers.

The Mechs page's turntable and frame playback are inspection controls. Their
speed is not a statement about a Mech's movement or firing cadence in combat.
