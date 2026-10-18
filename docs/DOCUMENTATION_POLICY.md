# Public documentation

Keep these documents focused on using the workshop and CLI, asset formats,
authoring workflows, the tooling's own code, and current capabilities and limits.

Campaign/combat rules, C17 game methods and gameplay schemas belong in the
Mechwarrior SDL-port repository. Link there rather than mirroring its guides or
keeping its method-name index here. Machine-readable conformance fixtures belong
under tests, not in the user-facing documentation.

Use the method names present in current source, without retired address suffixes.
Explain gameplay in BattleTech terms; graphics and audio use their own domain
terminology. Keep examples, paths and CLI recipes working.

Historical conversion reports, transfer inventories, audit journals and native
register/address research belong in the reverse-engineering repository. Do not
copy them into this repository or maintain a local documentation-history tree.

Run `python tools/Test-Documentation.py` after updating documentation and
`python tools/Test-PublicTree.py` before publishing files. Original game files,
private saves and extracted asset dumps must never enter the public source tree.
