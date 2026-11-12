"""Interactive launcher for the workspace's three local world profiles."""
import argparse
import sys
from pathlib import Path

if __package__:
    from .editor import main, WORKSPACE, REPO, DATA_REPO
else:
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
    from world_editor.editor import main, WORKSPACE, REPO, DATA_REPO


def launch():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profile', choices=['classic','stormforged','modern'])
    parser.add_argument('--no-browser', action='store_true')
    parser.add_argument('--port', type=int, default=0)
    args=parser.parse_args()
    modern=DATA_REPO/'artifacts/modern-client-test/runtime'
    profiles={
        'classic':(WORKSPACE/'Classic/runtime/data/opendaoc.sqlite3.db', WORKSPACE/'Classic/runtime/client-opendaoc/app'),
        'stormforged':(WORKSPACE/'Stormforged/runtime/data/opendaoc.sqlite3.db', WORKSPACE/'Stormforged/runtime/client-opendaoc/app'),
        'modern':(modern/'data/opendaoc.sqlite3.db', modern/'client-modern/app'),
    }
    if not args.profile:
        print('\n  DAoC WORLD WORKSHOP\n  3D placement editor and client world synchronization\n')
        print('  1. Classic\n  2. Stormforged\n  3. Modern client test\n')
        print('  The editor starts no game/server. Edits stay pending until saved.\n')
        choice=input('  Select world [1]: ').strip() or '1'
        if choice not in ('1','2','3'):
            raise SystemExit('Choose 1, 2, or 3.')
        args.profile={'1':'classic','2':'stormforged','3':'modern'}[choice]
    db,client=profiles[args.profile]
    targets=[]
    for _,candidate in profiles.values():
        if candidate!=client and candidate.is_dir() and candidate not in targets:
            targets.append(candidate)
    # The same saved-state folder the launcher's WORLD EDITOR button uses (<game>/runtime/world-workshop-data).
    argv=['--db',str(db),'--client',str(client),'--state',str(db.parent.parent/'world-workshop-data'), '--port',str(args.port)]
    for target in targets:
        argv+=['--target-client',str(target)]
    if args.no_browser:
        argv.append('--no-browser')
    main(argv)


if __name__=='__main__':
    try:
        launch()
    except (ValueError, OSError) as exc:
        print('\nCould not start the editor: '+str(exc))
        raise SystemExit(1)
