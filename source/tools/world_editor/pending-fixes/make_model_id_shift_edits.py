"""Writes the World Workshop edits that move monsters off the IDs the 1.130 client reused.

    python make_model_id_shift_edits.py <opendaoc.sqlite3.db> <out.edits.json>

The older client (client-opendaoc) has duplicate creature rows at 1380-1392 and the classic dragon/giant/glimmer rows
at 2382-2395. The 1.130 client dropped them and reused the IDs: tournament statues at 2382-2395, race shades and a
mummy at 1380-1390, no row at 1391-1392. Every new ID below has the same row in both clients:
- 1380-1392 go to the twin rows with the same mesh and skins (no change in look), except where the world was written
  for the 1.130 meaning: "shade" on 1380, the ancient bound atlanteans (mummy) on 1387, the svartalfs on 1389 (their
  templates pair 1389 with 621, Svartalf Male) and the Midgard svartalf and named NPCs on 1390 (paired with 622,
  Svartalf Female). On 1390 only huldu, dryads and the users outside Midgard move;
- 2382-2395 have no twin in 1.130, so they go to the revamped dragon, rock giant and glimmer rows.
Mob rows are changed by model; NpcTemplate rows one by one, since Model can list several models ("a;b").
"""
import json
import re
import sqlite3
import sys

MODEL_MAP = {
    1382: 119,   # Giant
    1383: 120,   # Forest Giant
    1384: 121,   # Ice Giant
    1385: 122,   # Fire Giant
    1386: 123,   # Worm
    1391: 128,   # Wilding (vine) Huldu
    1392: 129,   # Carrion Eater 2 (spider)
    2382: 612,   # Wolf Dragon -> Midgard Wolf Dragon (revamped)
    2383: 613,   # Glimmer Dragon -> Hibernian Glimmer Dragon (revamped)
    2384: 614,   # Stone Dragon -> Albion Stone Dragon (revamped)
    2385: 615,   # Skinny Giant -> Rock Giant (revamped)
    2386: 616,   # Alternate Skinny Giant -> Rock Giant with armor (revamped)
    2388: 618,   # Glimmer Caster 1 -> revamped Glimmer Caster 1
    2389: 619,   # Glimmer Ghoul -> Glimmer Ghoul
    2390: 620,   # Glimmer Knight 1 -> Glimmer Knight 1 (revamped)
    2394: 627,   # Glimmer Caster 2 -> revamped Glimmer Caster 2
    2395: 628,   # Glimmer Knight 2 -> Glimmer Knight 2 (revamped)
}


# 1390 (Brown Huldu in the old client, Svartalf Female Shade in 1.130) is split by row.
HULDU_MODEL, HULDU_NEW = 1390, 127
MIDGARD = 100


def huldu_row_moves(name, region):
    """True for a 1390 mob that means the huldu: Midgard keeps its svartalfs and named NPCs on the shade."""
    lowered = (name or '').lower()
    return region != MIDGARD or lowered.startswith('huldu') or lowered.startswith('dryad')


def remap_list(value):
    """Rewrites each model number in a template's Model text, keeping its separators."""
    return re.sub(r'\d+', lambda m: str(MODEL_MAP.get(int(m.group()), m.group())), value)


def main(db_path, out_path):
    db = sqlite3.connect(f'file:{db_path}?mode=ro', uri=True)
    edits = []
    for old, new in sorted(MODEL_MAP.items()):
        if db.execute('SELECT 1 FROM Mob WHERE Model = ? LIMIT 1', (old,)).fetchone():
            edits.append({'table': 'Mob', 'where': {'Model': old}, 'set': {'Model': new}})
    for key, name, region in db.execute('SELECT Mob_ID, Name, Region FROM Mob WHERE Model = ?', (HULDU_MODEL,)):
        if huldu_row_moves(name, region):
            edits.append({'table': 'Mob', 'key': key, 'set': {'Model': HULDU_NEW}})
    # Every NpcTemplate on 1390 is a Midgard svartalf (several pair it with 622), so templates keep 1390.
    for key, model in db.execute('SELECT NpcTemplate_ID, Model FROM NpcTemplate WHERE Model IS NOT NULL'):
        fixed = remap_list(str(model))
        if fixed != str(model):
            edits.append({'table': 'NpcTemplate', 'key': key, 'set': {'Model': fixed}})
    with open(out_path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(edits, f, indent=1)
        f.write('\n')
    print(f'{len(edits)} edits written to {out_path}')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
