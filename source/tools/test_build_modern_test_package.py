"""Checks of the modern-test package builder's file selection and server configuration (build_modern_test_package.py)."""
import sqlite3
import tempfile
import unittest
from pathlib import Path
import build_modern_test_package as b


ROD, ROD_SETTING = {4927: 442, 4928: 442}, {'fishing_pole_model': '442'}


class ModernPackageTests(unittest.TestCase):
    def test_no_client_profile_or_personal_file_is_copied(self):
        for path in ['client-modern/app/game.dll', 'modern-client.json', 'client-patch.json', 'account.txt', 'logs/server.log',
                     'data/opendaoc.sqlite3.db', 'server/logs/x.txt', 'modern-profile-verification.json',
                     'client-modern-armortest/app/camelot.exe', 'START ARMOR TEST CLIENT.cmd', 'overlay.json']:
            self.assertFalse(b.keep_runtime(Path(path)), path)
        for path in ['server/GameServer.dll', 'server/navmesh/roads.json', 'OfflineDAoC.exe', 'server/config/serverconfig.xml']:
            self.assertTrue(b.keep_runtime(Path(path)), path)

    def test_the_release_stamp_names_the_version_and_package(self):
        text = b.release_stamp('DR-dev-0.1d Fishing Update', 'DividedRealms-DR-dev-0.1d-Fishing-Update', 'abc123')
        info = dict(line.split('=', 1) for line in text.splitlines())
        self.assertEqual(info['Version'], 'DR-dev-0.1d')
        self.assertEqual(info['Label'], 'DR-dev-0.1d Fishing Update')
        self.assertEqual(info['Package'], 'DividedRealms-DR-dev-0.1d-Fishing-Update')
        self.assertEqual(info['Repository'], 'rambodolphin13/DividedRealms')
        self.assertEqual(info['Commit'], 'abc123')
        self.assertTrue(text.endswith('\r\n'))
        self.assertNotIn('natex', text.lower())

    def test_the_world_path_becomes_package_relative(self):
        with tempfile.TemporaryDirectory() as folder:
            config = Path(folder) / 'serverconfig.xml'
            config.write_text('<root><Server><IP>127.0.0.1</IP><Port>10330</Port><AutoAccountCreation>False</AutoAccountCreation>'
                              '<DBConnectionString>Data Source=C:\\Users\\someone\\runtime\\data\\opendaoc.sqlite3.db;Version=3'
                              '</DBConnectionString></Server></root>', encoding='utf-8')
            b.server_config(config)
            text = config.read_text(encoding='utf-8')
            self.assertIn('Data Source=../data/opendaoc.sqlite3.db;Version=3', text)
            self.assertIn('<AutoAccountCreation>True</AutoAccountCreation>', text)
            self.assertNotIn('someone', text)

    def test_a_private_model_is_swapped_for_its_stock_one(self):
        con = sqlite3.connect(':memory:')
        con.execute('CREATE TABLE ItemTemplate (Id_nb TEXT, Model INTEGER)')
        con.execute('CREATE TABLE ServerProperty (`Key` TEXT, Value TEXT, DefaultValue TEXT)')
        con.executemany('INSERT INTO ItemTemplate VALUES (?, ?)', [('pole_a', 4927), ('pole_b', 4928), ('staff', 442), ('sword', 4)])
        con.executemany('INSERT INTO ServerProperty VALUES (?, ?, ?)', [('fishing_pole_model', '4927', '442'), ('fishing_sits', 'True', 'True')])
        self.assertEqual(b.use_stock_models(con, ROD, ROD_SETTING), dict(item_templates=2, server_properties=1))
        self.assertEqual(con.execute('SELECT COUNT(*) FROM ItemTemplate WHERE Model IN (4927, 4928)').fetchone()[0], 0)
        self.assertEqual(dict(con.execute('SELECT Id_nb, Model FROM ItemTemplate')), dict(pole_a=442, pole_b=442, staff=442, sword=4))
        self.assertEqual(dict(con.execute('SELECT `Key`, Value FROM ServerProperty')), dict(fishing_pole_model='442', fishing_sits='True'))
        self.assertEqual(b.use_stock_models(con, ROD, ROD_SETTING), dict(item_templates=0, server_properties=0))

    def test_an_item_on_a_private_model_the_patch_lacks_is_found(self):
        # DR-dev-0.1d shipped the Storm Regalia on objects 4929-4942, which no release client has.
        con = sqlite3.connect(':memory:')
        con.execute('CREATE TABLE ItemTemplate (Id_nb TEXT, Model INTEGER)')
        con.executemany('INSERT INTO ItemTemplate VALUES (?, ?)', [('sword', 4), ('pole', 4927), ('tara_regalia_hauberk', 4950),
                                                                   ('storm_regalia_hauberk', 4929), ('cloak_25th_anniversary_albion', 4924)])
        self.assertEqual(b.models_without_art(con, 4923, {4927, 4928, 4950}), [('storm_regalia_hauberk', 4929)])

    def test_host_for_friends_is_switched_off(self):
        with tempfile.TemporaryDirectory() as folder:
            config = Path(folder) / 'serverconfig.xml'
            config.write_text('<root><Server>\n<Port>10330</Port>\n<IP>0.0.0.0</IP>\n<RegionIP>127.0.0.1</RegionIP>\n'
                              '<UdpIP>0.0.0.0</UdpIP>\n<EnableUPnP>True</EnableUPnP>\n<PublicIP>auto</PublicIP>\n'
                              '<AutoAccountCreation>False</AutoAccountCreation>\n<DBConnectionString>Data Source=x.db;Version=3</DBConnectionString>\n</Server></root>',
                              encoding='utf-8')
            b.server_config(config)
            text = config.read_text(encoding='utf-8')
            for line in ('<IP>127.0.0.1</IP>', '<UdpIP>127.0.0.1</UdpIP>', '<EnableUPnP>False</EnableUPnP>',
                         '<RegionIP>127.0.0.1</RegionIP>'):
                self.assertIn(line, text)
            self.assertNotIn('PublicIP', text)
        self.assertFalse(b.keep_runtime(Path('server/config/serverconfig.before-host-for-friends.xml')))


if __name__ == '__main__':
    unittest.main()
