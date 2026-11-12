"""Static checks of the class-slot patch against the pristine 1.130 client (skipped when it is absent)."""
import hashlib
import struct
import unittest
from pathlib import Path

import patch_class_slots as slots

PRISTINE = Path(__file__).resolve().parents[3]/'artifacts/modern-client-test/backups/client-bundle-20261021-165832-708287/game.original.dll'


class ClassSlotPatchTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not PRISTINE.is_file():
            raise unittest.SkipTest('Pristine 1.130 game.dll is not available')
        cls.original = PRISTINE.read_bytes()
        cls.patched, cls.layout = slots.patch(cls.original)

    def offset(self, data, va):
        pe = struct.unpack_from('<I', data, 60)[0]
        opt = pe+24
        count = struct.unpack_from('<H', data, pe+6)[0]
        table = opt+struct.unpack_from('<H', data, pe+20)[0]
        for i in range(count):
            _, vsize, rva, size, raw = struct.unpack_from('<8sIIII', data, table+40*i)
            if rva <= va-slots.BASE < rva+max(size, vsize):
                return raw+va-slots.BASE-rva
        raise ValueError(hex(va))

    def test_rejects_other_inputs(self):
        with self.assertRaises(ValueError):
            slots.patch(self.original[:-1]+b'\x01')

    def test_deterministic(self):
        again, _ = slots.patch(self.original)
        self.assertEqual(hashlib.sha256(again).digest(), hashlib.sha256(self.patched).digest())

    def test_only_hooks_and_headers_change(self):
        hooks = {slots.NAME_HOOK: 6, slots.STAT_HOOK: 10, slots.RESIST_HOOK: 6, slots.ADAPTER_HOOK: 5,
                 slots.FLIGHT_HOOK: 5}
        hooks.update({site: len(bytes.fromhex(original)) for site, original, _, _ in slots.FLIGHT_ANIM_HOOKS})
        if slots.TUATHA_HOVER:
            hooks.update({slots.HOVER_SELECT[0]: 7, slots.HOVER_WORLD[0]: 7})
            hooks.update({site: len(bytes.fromhex(original)) for site, original, _, _ in slots.VAMPIIR_ONLY})
            hooks[slots.HOVER_SPEED[0]] = len(bytes.fromhex(slots.HOVER_SPEED[1]))
        hooks.update({realm['hook']: len(bytes.fromhex(realm['original'])) for realm in slots.REALMS.values()})
        allowed = set()
        for va, size in hooks.items():
            pos = self.offset(self.original, va)
            allowed.update(range(pos, pos+size))
        allowed.add(self.offset(self.original, slots.CLASS_ARRAY_SIZE[0])+4)  # the array-size immediate
        allowed.add(self.offset(self.original, slots.RIDE_ANIM_EDIT[0]))  # jne -> jmp opcode
        for va, before, after in slots.SCREEN_EDITS + slots.CAMERA_MAX_EDITS + slots.FLIGHT_SPEED_EDITS:
            pos = self.offset(self.original, va)
            allowed.update(pos+k for k in range(len(bytes.fromhex(before))))
        header_end = 0x400
        changed = [i for i in range(len(self.original)) if self.original[i] != self.patched[i] and i >= header_end]
        self.assertTrue(set(changed) <= allowed)
        for va in hooks:
            pos = self.offset(self.patched, va)
            self.assertEqual(self.patched[pos], 0xE9, hex(va))

    def test_new_section_holds_names_and_entries(self):
        text = self.layout['text']
        for slot in slots.SLOTS:
            pos = self.offset(self.patched, text[slot.class_id, 'name'])
            self.assertEqual(self.patched[pos:pos+len(slot.name)+1], slot.name.encode()+b'\0')
        for realm in slots.REALMS.values():
            stub = self.offset(self.patched, self.layout['stubs'][[k for k, v in slots.REALMS.items() if v is realm][0]])
            code = self.patched[stub:stub+0x200]
            for slot in realm['slots']:
                self.assertIn(bytes([0x6a, slot.class_id, 0xff, 0x75, 0x08]), code)  # push class; push [ebp+8]
        self.assertEqual([s.class_id for s in slots.SLOTS], [63, 64, 65, 66, 67])
        self.assertEqual([s.class_id for s in slots.REALMS['Albion']['slots']], [65, 66], 'Death Knight after Pendragon')
        self.assertEqual([s.class_id for s in slots.REALMS['Hibernia']['slots']], [67], 'Tuatha takes the Sluaghbinder entry')

    def test_flight_stub_sets_and_clears_only_the_flight_flag(self):
        stub = self.layout['stubs']['flight']
        pos = self.offset(self.patched, stub)
        flag = struct.pack('<I', slots.FLIGHT_FLAG)
        code = self.patched[pos:pos+44]
        self.assertEqual(code[:5], bytes.fromhex('9c807b0101'))                   # pushfd; cmp byte [ebx+1],1
        self.assertEqual(code[7:17], bytes.fromhex('c705') + flag + (1).to_bytes(4, 'little'))
        self.assertEqual(code[17:21], bytes.fromhex('807b0102'))
        self.assertEqual(code[23:33], bytes.fromhex('c705') + flag + bytes(4))
        self.assertEqual(code[33:39], bytes.fromhex('9db89a000000'))             # popfd; replayed mov eax,0x9a
        back = stub + 39 + 5 + struct.unpack_from('<i', code, 40)[0]
        self.assertEqual((code[39], back), (0xE9, slots.FLIGHT_BACK))
        hook = self.offset(self.patched, slots.FLIGHT_HOOK)
        self.assertEqual(slots.FLIGHT_HOOK + 5 + struct.unpack_from('<i', self.patched, hook+1)[0], stub)

    def test_flight_anim_hooks_return_and_branch_on_the_flight_flag(self):
        flag = struct.pack('<I', slots.FLIGHT_FLAG)
        zero = self.layout['section_va'] + slots.FLIGHT_ZERO
        for site, original, back, kind in slots.FLIGHT_ANIM_HOOKS:
            stub = self.layout['stubs'][f'flight_anim_{site:x}']
            hook = self.offset(self.patched, site)
            self.assertEqual(site + 5 + struct.unpack_from('<i', self.patched, hook+1)[0], stub, hex(site))
            pos = self.offset(self.patched, stub)
            code = self.patched[pos:pos+64]
            self.assertIn(bytes.fromhex('833d') + flag + bytes(1), code, hex(site))
            if kind in ('mode', 'mount'):
                self.assertIn(struct.pack('<I', zero), code, hex(site))
            jumps = [stub + i + 5 + struct.unpack_from('<i', code, i+1)[0] for i in range(len(code)-5) if code[i] == 0xE9]
            self.assertIn(back, jumps, hex(site))
        pos = self.offset(self.patched, zero)
        self.assertEqual(self.patched[pos:pos+0x40], bytes(0x40))

    def test_flight_speeds_are_lowered(self):
        values = [struct.unpack_from('<I', self.patched, self.offset(self.patched, va) + len(bytes.fromhex(after)) - 4)[0]
                  for va, before, after in slots.FLIGHT_SPEED_EDITS]
        self.assertEqual(values, [slots.FLIGHT_SPEED, slots.FLIGHT_SPEED * 11 // 20])

    def test_camera_zoom_maximums_are_raised(self):
        for va, before, after in slots.CAMERA_MAX_EDITS:
            pos = self.offset(self.patched, va)
            self.assertEqual(struct.unpack_from('<f', self.patched, pos+3)[0], slots.CAMERA_MAX, hex(va))

    def test_ride_anim_takes_the_1127_path(self):
        pos = self.offset(self.patched, slots.RIDE_ANIM_EDIT[0])
        self.assertEqual(self.patched[pos:pos+2], bytes.fromhex('eb5f'))  # jmp 0x42BE75

    def test_class_array_covers_new_slots(self):
        pos = self.offset(self.patched, slots.CLASS_ARRAY_SIZE[0])
        self.assertEqual(self.patched[pos:pos+5], bytes.fromhex('8d7e346a44'))
        self.assertGreater(0x44, max(s.class_id for s in slots.SLOTS))

    def test_vampiir_flag_untouched_while_hover_is_off(self):
        if slots.TUATHA_HOVER:
            self.skipTest('Tuatha hover is on')
        for site in [slots.HOVER_SELECT[0], slots.HOVER_WORLD[0]] + [s for s, *_ in slots.VAMPIIR_ONLY]:
            pos = self.offset(self.patched, site)
            self.assertEqual(self.patched[pos:pos+6], self.original[pos:pos+6], hex(site))

    def test_hover_only_class_gets_its_own_flag_value(self):
        if not slots.TUATHA_HOVER:
            self.skipTest('Tuatha hover is off')
        select = self.offset(self.patched, self.layout['stubs']['hover_select'])
        self.assertEqual(self.patched[select:select+21], bytes.fromhex('8a461d 3c3a 7408 3c43 7408 30c0 eb06 b001 eb02 b002'))
        pos = self.offset(self.patched, 0x417F9B)
        self.assertEqual(self.patched[pos:pos+4], self.original[pos:pos+4], 'the class 58 compare stays original')
        for site, original, back, kind in slots.VAMPIIR_ONLY:
            pos = self.offset(self.patched, site)
            self.assertEqual(self.patched[pos], 0xE9, hex(site))

    def test_screen_edits_move_every_button_access(self):
        for va, before, after in slots.SCREEN_EDITS:
            pos = self.offset(self.patched, va)
            self.assertEqual(self.patched[pos:pos+len(bytes.fromhex(after))], bytes.fromhex(after), hex(va))
            self.assertNotIn(struct.pack('<I', slots.OLD_ARRAY), bytes.fromhex(after))
        new = self.offset(self.patched, 0x5999DD)
        self.assertEqual(struct.unpack_from('<I', self.patched, new+1)[0], slots.NEW_ARRAY + 4*slots.BUTTONS)

    def test_layout_adds_buttons_for_every_new_slot(self):
        stock = PRISTINE.parents[3]/'classslots-staging/character_creation.original.xml'
        installed = PRISTINE.parents[2]/'runtime/client-modern/app/pregame/character_creation.xml'
        layout = stock if stock.is_file() else installed
        source = layout.read_bytes()
        if hashlib.sha256(source).hexdigest() != slots.LAYOUT_SUPPORTED:
            self.skipTest('Installed layout is already patched')
        text = slots.patch_layout(source).decode('latin-1')
        for index in range(slots.BUTTONS):
            self.assertEqual(text.count(f'<Adapter>class_{index}_text</Adapter>'), 1)
            self.assertEqual(text.count(f'<ControlId>{1021+index}</ControlId>'), 1)
        with self.assertRaises(ValueError):
            slots.patch_layout(text.encode('latin-1'))

    def test_extra_adapters_are_registered_with_writable_slots(self):
        text = self.layout['text']
        stub = self.offset(self.patched, self.layout['stubs']['adapters'])
        code = self.patched[stub:stub+0x80]
        self.assertEqual(code.count(b'\x56\xe8'), slots.BUTTONS-16)  # push owner; call register
        for index in range(16, slots.BUTTONS):
            self.assertIn(f'class_{index}_text'.encode()+b'\0', self.patched)
        pe = struct.unpack_from('<I', self.patched, 60)[0]
        count = struct.unpack_from('<H', self.patched, pe+6)[0]
        table = pe+24+struct.unpack_from('<H', self.patched, pe+20)[0]
        name, *_, flags = struct.unpack_from('<8sIIIIIIHHI', self.patched, table+40*(count-1))
        self.assertEqual(name, b'.slots\0\0')
        self.assertTrue(flags & 0x80000000, 'section must be writable for the adapter slots')

    def test_mauler_entries_stay_stock(self):
        for va in (0x5B0AE5, 0x5B21F7, 0x5B348E):  # mov [ebp-0x4c], 'Mauler'
            pos = self.offset(self.patched, va)
            self.assertEqual(self.patched[pos:pos+7], self.original[pos:pos+7])
            self.assertEqual(struct.unpack_from('<I', self.patched, pos+3)[0], 0x940CEC)


if __name__ == '__main__':
    unittest.main()
