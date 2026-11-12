# Bountycraft: the 45 items the 1.126 export lacks (2026-11-08)

Same generator as `../bountycraft-2026-11-08` (`../make_bountycraft_edits.py`), run on the world after that patch with
the newer records as an extra source:

    python -I make_bountycraft_edits.py <world.db> <client app> <daoc_item_database_combined.zip> <out dir> \
        "DAoC Offline\Research\Sources\Herald-Item-Database-1.126\online-herald-bountycraft-2026-11-08.json"

The extra file holds the "Raw Item JSON" of `https://tools.camelotherald.com/item/view/<id>` for the 45 official ids
the client recipes name (fetched 2026-11-08, one request a second; same format as the 1.126 export). They are
matched by id alone: the client's recipe names are older than the items ("Cabalist - Unyielding Majestic Vest" is
official 24081 "Unyielding Majestic Robe").

275 row edits: 45 ItemTemplate (Aurulite "Unyielding" chests, Frozen "Freezing" chests, Mantles of Samhain, the
Dark Magister / Dark Queen / Dark Defender pieces, Pictslayer Shillelagh, Abagath and Ancient Priest maces...),
45 CraftedItem, 185 CraftedXItem. Applied to the test world 2026-11-08 19:57, undo `20261108-195753-cfbe2b1f`
(owner OK "Yes, fetch them").

Sandbox `bountycraft`: `scenarios/bountycraft-online.json` crafts the Unyielding Majestic Robe (recipe 1733) at a
Camelot forge at 100%, materials used, no log errors. Not played.

Gaps: the same unbuilt item spells as the first patch (resistance enhancement, illusions, defensive procs, stun...;
`summary.json`); mythical parry / block / realm point bonuses (codes 60, 62, 72) on 3 items are not mapped. 1 recipe
(1726 Jerkin of Demonic Intent) stays skipped: the world item already has other ingredients.
