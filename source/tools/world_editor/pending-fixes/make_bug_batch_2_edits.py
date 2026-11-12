"""Write bug-batch-2-2026-11-08.edits.json: NPC class names the build does not have (DR-039).

Every row named here spawns today as a plain GameNPC (or keeps its own brain), so trainers do not train and
rechargers do not recharge. Each target class is the one the same NPC already uses elsewhere in this world:
  Mob / NpcTemplate ClassType  DOL.GS.Scripts.Recharger        -> DOL.GS.Recharger
                               DOL.GS.Trainer.RogueTrainer     -> AlbionRogueTrainer / MidgardRogueTrainer (by realm;
                                                                  Jucla and Master Reginald already spawn as these)
                               DOL.GS.Trainer.MaulerTrainer    -> HiberniaMaulerTrainer (Consul Addreoc spawns as it)
                               DOL.GS.AlchemistMaster          -> DOL.GS.AlchemistsMaster (Armod Dag spawns as it)
                               '0' (drakoran mage)             -> DOL.GS.GameNPC
  Mob Brain                    names with no class (the NPC class sets its own brain, or the default is used) -> ''
Left alone: templates no Mob uses whose class has no counterpart here (DOL.GS.GamePC, DOL.Storm.*).

Run: python make_bug_batch_2_edits.py <world db>   (reads only)
"""
import json
import sqlite3
import sys
from pathlib import Path

ALB, MID = 'DOL.GS.Trainer.AlbionRogueTrainer', 'DOL.GS.Trainer.MidgardRogueTrainer'
TEMPLATE_ROGUE = {'Master Odon': ALB, 'Master Reginald': ALB, 'Bald': MID, 'Jucla': MID, 'Glum': MID, 'Hrut': MID}
DEAD_BRAINS = ('DOL.AI.Brain.ParthananFarmController', 'DOL.GS.MainTeleporterBrain', 'DOL.AI.Brain.AggressiveBrain',
               'GiantSporiteClusterBrain')


def main(db):
    con = sqlite3.connect(f'file:{db}?mode=ro', uri=True)
    edits = []

    def mob(mob_id, **cols):
        edits.append({'table': 'Mob', 'key': mob_id, 'set': cols})

    def template(row_id, **cols):
        edits.append({'table': 'NpcTemplate', 'key': row_id, 'set': cols})

    for mob_id, realm, cls in con.execute("select Mob_ID, Realm, ClassType from Mob where ClassType in "
                                          "('DOL.GS.Scripts.Recharger', 'DOL.GS.Trainer.RogueTrainer', '0') order by Mob_ID"):
        if cls == 'DOL.GS.Scripts.Recharger':
            mob(mob_id, ClassType='DOL.GS.Recharger')
        elif cls == '0':
            mob(mob_id, ClassType='DOL.GS.GameNPC')
        else:
            mob(mob_id, ClassType={1: ALB, 2: MID}[realm])

    for mob_id, in con.execute(f"select Mob_ID from Mob where Brain in ({','.join('?' * len(DEAD_BRAINS))}) order by Mob_ID",
                               DEAD_BRAINS):
        mob(mob_id, Brain='')

    for row_id, name, cls in con.execute("select NpcTemplate_ID, Name, ClassType from NpcTemplate where ClassType in "
                                         "('DOL.GS.Scripts.Recharger', 'DOL.GS.Trainer.RogueTrainer', "
                                         "'DOL.GS.Trainer.MaulerTrainer', 'DOL.GS.AlchemistMaster') order by NpcTemplate_ID"):
        if cls == 'DOL.GS.Scripts.Recharger':
            template(row_id, ClassType='DOL.GS.Recharger')
        elif cls == 'DOL.GS.Trainer.RogueTrainer':
            template(row_id, ClassType=TEMPLATE_ROGUE[name])
        elif cls == 'DOL.GS.Trainer.MaulerTrainer':
            template(row_id, ClassType='DOL.GS.Trainer.HiberniaMaulerTrainer')
        else:
            template(row_id, ClassType='DOL.GS.AlchemistsMaster')

    out = Path(__file__).with_name('bug-batch-2-2026-11-08.edits.json')
    out.write_text(json.dumps(edits, indent=1) + '\n', encoding='utf-8')
    print(f'{len(edits)} edits -> {out.name}')


if __name__ == '__main__':
    main(sys.argv[1])
