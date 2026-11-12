"""Hash-guarded 1.130 client patch: native class slots 63-67 and stock Maulers.

Starts from the pristine 1.130 game.dll (the input of patch_modern_client.py) and adds, in one new
.slots PE section reached from hash-checked hooks:

  * class names 63-65 in the client's 100-slot class-name table (0xD92578 + 30*class);
  * a character-creation entry per class, appended after its realm's Mauler entry, built exactly like
    that Mauler entry (same struct fields, race-list ABI and 0x5AFC27 registration call):
      63 Sluaghbinder (Hibernia), 64 Storm Sovereign (Midgard), 65 Pendragon Sovereign (Albion),
      66 Death Knight (Albion, the modern live Necromancer, registered right after the Pendragon entry),
      67 Tuatha Sovereign (Hibernia; it takes the Sluaghbinder's creation entry, class 63 keeps its name);
  * (off: TUATHA_HOVER) the Vampiir hover alone for the player's own Tuatha Sovereign;
  * the creation primary/secondary/tertiary stat highlight for 63-66 (switch at 0x5B400A);
  * Codex's Storm Sovereign resist-display fix (0x41A639), which is class-independent;
  * the stable-horse rider fix (0x42BE14, see RIDE_ANIM_EDIT): riders on NPC steeds sit instead of standing;
  * server-driven native flight (FLIGHT_HOOK) for flying mounts: DebugMode packet byte 1 = 1/2 sets/clears
    the client's own flight flag, for any player (the classic client's patch_observer_flight.py was GM-only);
  * flying mounts keep their idle animation (FLIGHT_ANIM_HOOKS) and fly at FLIGHT_SPEED (FLIGHT_SPEED_EDITS);
  * a longer camera zoom (CAMERA_MAX_EDITS): the distance clamp's 500 / 1500 maximums become CAMERA_MAX;
  * the creation screen's class-indexed entry array (container +0x34) sized for 68 classes instead of 63.
    Without this, registering class 63 writes one slot past the 0xFC-byte array and corrupts the heap
    (first test install crashed opening character creation);
  * the creation screen's class buttons extended from 16 to 20 per realm (second test: Albion and Hibernia,
    which already had 16 classes, crashed looking up a missing class_16_text button). See SCREEN_EDITS and
    patch_layout(), which adds buttons class_16..class_19 to pregame/character_creation.xml.

The three Mauler entries, names and race lists are left stock, so the Mauler slots are real Maulers
again. The server must use classes/native_custom_class_slots with this client. No game is executed.
Evidence and addresses: docs/CLIENT-CLASS-SLOTS-FEASIBILITY.md.
"""
import argparse
import hashlib
import struct
from pathlib import Path

SUPPORTED = '3915a495253040a4cdfc79d265a8d268797304eb46b9a34d51a88d49a8445022'
# Earlier native-slot builds installers may replace with the current one (the current build's own hash
# is computed from patch()): Death Knight slots 63-66, the first Tuatha build (full Vampiir flag for 67),
# Tuatha without hover (3954..., the build before the stable-horse rider fix), and the hover-only test build.
KNOWN_NATIVE_CLIENTS = {
    '97755b32677f4482d410bcb2fca08c7918265854a869a6a298fdc4e3dec89d84',
    '26392ed8c8948ff5f5604b24e54c5ada5657bf50719b3502bc0c8fd1820452dd',
    '3954474f9a3b120b36fc350aea8978c7d2c211c227459364937f548bb2b5bc8f',
    'e70fc1e07b74a610d42abeb137ebf741c22d66eb4787cb9093fa7c59256a4f93',
    '9e09af8127252b5995f4e508e7cecd69465a4b74d5d34d6294b34701b6dec174',  # + stable-horse rider fix
    '1806cc4c3df0e8abdb8365ee32987ebf200f90df4eaa53eec3ed1df24f6e009f',  # + native flight, camera zoom
    'ec754fbbed644f1b509cf507ff0051930755f180b28f75e80e582fc723bf8785',  # + flight idle anims, speed 350
    '40f29d2ad5693e6ef9d1c9a4d0558f0834565f10fe01519fd4463f323e8bf775',  # flight speed 450
}
BASE = 0x400000
NAME_TABLE = 0xD92578
NAME_SLOT = 30
ADD_RACE = 0x5B440D      # list insertion: EAX = race list, one pushed pointer to the race id
COPY_LIST = 0x59F8D4     # thiscall copy of the race list into the by-value argument
REGISTER_CLASS = 0x5AFC27
CLEAR_LIST = 0x515A30    # ESI = race list
STRINGS = 0xD93264       # string manager used by the name initializer

# Stat indices used by the client's creation stat switch (from its Paladin and Mauler cases).
STR, CON, DEX, QUI, INT, PIE, EMP, CHA = range(8)


class Slot:
    def __init__(self, class_id, name, description, races, stats):
        self.class_id, self.name, self.description, self.races, self.stats = class_id, name, description, races, stats


SLUAGHBINDER = Slot(63, 'Sluaghbinder',
                    'A Hibernian warrior-binder who calls the restless sluagh host to fight beside it, wielding rot and the old cairn oaths.',
                    (9, 10), (STR, INT, CON))
STORM_SOVEREIGN = Slot(64, 'Storm Sovereign',
                       'A royal master of thunder, weapons and stealth, accompanied by a loyal Storm Wolf.',
                       (5, 6, 7, 8, 14, 17, 20), (STR, PIE, CON))
PENDRAGON = Slot(65, 'Pendragon Sovereign',
                 'Albion royal champion of holy fire, weapons and stealth, accompanied by a loyal Royal Lion.',
                 (1, 2, 3, 4, 13, 16, 19), (STR, PIE, CON))
DEATH_KNIGHT = Slot(66, 'Death Knight',
                    'An Albion servant of Arawn who fights directly through Decrepit, Chthonic and Spirit forms, with death servants at its side.',
                    (1, 4, 13), (INT, DEX, QUI))
TUATHA = Slot(67, 'Tuatha Sovereign',
              'Hibernian royal sovereign of the Tuatha De Danann: spirit magic, weapons and stealth, accompanied by a loyal Faerie Drake.',
              (9, 10, 11, 12, 15, 18, 21), (STR, PIE, CON))
SLOTS = (SLUAGHBINDER, STORM_SOVEREIGN, PENDRAGON, DEATH_KNIGHT, TUATHA)

# Per realm: hook right after the Mauler entry's list clear, the original bytes replaced there, where to
# return, and how the Mauler entry set its register-held fields in that function.
#   flags: value for [ebp-0x44]/[ebp-0x40]/[ebp-0x1c]; field18: [ebp-0x18]; realm/count: pushed args.
REALMS = {
    'Albion':   dict(slots=(PENDRAGON, DEATH_KNIGHT), hook=0x5B0B85, original='8365b8006a07', back=0x5B0B8B,
                     flags='ebx', field18='edi', realm='ebx', count=3),
    'Midgard':  dict(slots=(STORM_SOVEREIGN,), hook=0x5B22A5, original='8365b8008365c400', back=0x5B22AD,
                     flags=1, field18=2, realm=2, count=3),
    'Hibernia': dict(slots=(TUATHA,), hook=0x5B352C, original='8365c4008365e000', back=0x5B3534,
                     flags=1, field18='ebx', realm='edi', count='edi'),
}
NAME_HOOK, NAME_ORIGINAL, NAME_BACK = 0x44EFD5, '8b0d6432d900', 0x44EFDB
STAT_HOOK, STAT_ORIGINAL, STAT_CASES, STAT_DEFAULT = 0x5B400A, '4a83fa3d0f8726010000', 0x5B4014, 0x5B413A
RESIST_HOOK, RESIST_ORIGINAL, RESIST_BACK = 0x41A639, '89904c93d400', 0x41A63F
# Creation container constructor: vector at +0x34 indexed by class ID (written at 0x5AFCF4, bounds-checked
# by size at 0x5B4236). 'lea edi,[esi+0x34]; push 0x3f' -> push 0x44 (classes 0-67).
CLASS_ARRAY_SIZE = 0x5AFA55, '8d7e346a3f', '8d7e346a44'
# 1.130's anim player (0x42BC86) gained a branch for attached personal horses. When a rider has none and the
# call is the NPC-steed ride path (mode 2, only from 0x42C945), it replays walk/run/idle from the rider's own
# speed (0x42BE10..0x42BE72), overwriting "Ride Horse" (slot 0x47): taxi riders stand. 1.127 has no such
# branch. jne -> jmp always takes the 1.127 path at 0x42BE75; personal mounts take the earlier branch.
RIDE_ANIM_EDIT = 0x42BE14, '755f', 'eb5f'
# Native flight. The DebugMode (0x21) handler stores byte 0 as the GM permission at 0x410EC0
# (movsx eax,[ebx]; mov [0xCDDC20],eax) and continues with mov eax,0x9a. The client's own flight flag is the
# dword the GM fly key toggles at 0x437475; its other readers (movement, gravity, 0x4B1FC4) test it alone.
# The server sends byte 1 = 1 to fly and 2 to land; 0 (every ordinary DebugMode packet) changes nothing.
FLIGHT_HOOK, FLIGHT_ORIGINAL, FLIGHT_BACK = 0x410EC8, 'b89a000000', 0x410ECD
FLIGHT_FLAG = 0xD90510
# Flight animation. Native flight sets the controller mode ([0xD91490]+0x30) to 3. The movement animation
# selector 0x42CAE6 handles idle/walk/run only for modes 0 and 6; other modes are looked up in a table that has
# no entry for 3, so nothing is chosen and rider and mount freeze. While the flight flag is set, the three
# local-player animation reads below see mode 0, a zeroed movement struct (speed 0) and no turn keys: rider and
# mount play their standing idle in the air. Each: (site, original bytes, return address, kind).
FLIGHT_ZERO = 0xC40          # section offset of a zeroed movement-struct stand-in (0x40 bytes)
FLIGHT_ANIM_HOOKS = [
    # anim player 0x42BC86, local rider: speed [mv+0x38] -> [ebp+8], keys [0xD90514]&0xF -> eax, jmp 0x42BCC6
    (0x42BC9E, 'a19c14d9008b4038894508a11405d90083e00feb13', 0x42BCC6, 'player'),
    (0x42D02E, '8b40308b359c14d900', 0x42D037, 'mode'),      # anim update 0x42CE87: mode, esi = movement struct
    (0x42D04D, '8b1d1405d900', 0x42D053, 'keys'),            # same, ebx = movement keys
    (0x42D1CD, '8b1d9c14d900', 0x42D1D3, 'mount'),           # the mount of the local rider: ebx = movement struct
    (0x42D1E6, '8b1d1405d900', 0x42D1EC, 'keys'),            # same, ebx = movement keys
]
# Flight speeds (0x438488 sets them while the flag is on): forward/vertical 2000 and the 1100 limit at 0x438D7F
# become FLIGHT_SPEED and the same 0.55 ratio. A level 35+ mount runs at 276 (191 * 145%).
FLIGHT_SPEED = 2 * 276   # twice a level 35+ mount's ground run (191 * 145%)
FLIGHT_SPEED_EDITS = [
    (0x4384A8, 'c74708d0070000', 'c74708' + struct.pack('<I', FLIGHT_SPEED).hex()),          # mov [edi+8],2000
    (0x438D7F, 'bf4c040000', 'bf' + struct.pack('<I', FLIGHT_SPEED * 11 // 20).hex()),       # mov edi,1100
]
# Camera distance setter (0x4AC33E; every zoom path calls it): the maximum is 500.0, or 1500.0 when two
# client checks pass, then clamp(value, 50.0, maximum). Both maximums become CAMERA_MAX. The /setcamera
# reset distance (0x99814C) is separate and unchanged.
CAMERA_MAX = 4000.0
CAMERA_MAX_EDITS = [
    (0x4AC354, 'c745fc0000fa43', 'c745fc' + struct.pack('<f', CAMERA_MAX).hex()),   # mov [ebp-4],500.0
    (0x4AC36D, 'c745fc0080bb44', 'c745fc' + struct.pack('<f', CAMERA_MAX).hex()),   # mov [ebp-4],1500.0
]
# Vampiir hover without the rest of the Vampiir look. The character record's "is Vampiir" byte (+0x15D,
# set from class == 58 at 0x402A66 and 0x417F9B) becomes the world object's flag +0x19C4 (0x433814).
# Animation and ground-contact code tests that flag for nonzero (the hover); the skin and model code
# (texture/tint builder 0x4174E2, 'Head%d' 0x47361F, model load 0x45013E and the getter 0x5ADFEB
# feeding 0x470241) reads it too. Class 67 gets the value 2; those skin readers then require exactly 1.
# 2026-11-06: on for a test build. Two more readers were found that still read 2 as Vampiir: 0x42236C feeds the
# character model builder 0x47044D (the likely cause of the Vampiir skin on an earlier hover test), and 0x59BD31/0x59BD5A
# (UI). And the hover-move animation (0x42CE14) needs speed over 250, so at a normal run a Vampiir runs; the hover-only
# class gets a threshold of 0 there instead (the owner wants the hover at any speed).
TUATHA_HOVER = True
HOVER_CLASS = 67
HOVER_SELECT = 0x402A66, '807e1d3a0f94c0', 0x402A6D       # cmp [esi+0x1d],0x3a; sete al
HOVER_WORLD = 0x417FD4, '0f94c0807dc001', 0x417FDB        # sete al; cmp byte [ebp-0x40],1
# (site, original bytes, return address, kind): 'cl'/'al' = mov reg,[obj+0x19c4]; others below.
VAMPIIR_ONLY = [
    (0x417608, '8a8ec4190000', 0x41760E, 'cl'),            # body texture choice
    (0x4176CF, '389ec4190000740a', 0x4176D7, 'cmp_je'),    # body tint, je 0x4176E1 when not Vampiir
    (0x417702, '8a86c4190000', 0x417708, 'al'),            # body tint colour
    (0x47361F, '8a8ec4190000', 0x473625, 'cl'),            # Head%d model
    (0x45013E, '3888c4190000', 0x450144, 'setne'),         # model load (preview), setne cl at 0x450148
    (0x5ADFF0, '8a80c4190000', 0x5ADFF6, 'al'),            # IsVampiir getter (model load 0x416AE2)
    (0x42236C, '389ec41900008945e80f95c1', 0x422378, 'model_setne'),  # isVampiir argument to model builder 0x47044D
    (0x59BD31, '80b8c4190000007511', 0x59BD3A, 'ui_jne'),  # UI setting skipped for a Vampiir
    (0x59BD5A, '80b8c4190000007511', 0x59BD63, 'ui_jne'),
]
VAMPIIR_UI_SKIP = {0x59BD31: 0x59BD4B, 0x59BD5A: 0x59BD74}
# Hover-move animation picker: speed in eax, object in ebx. Over 0xFA it goes on to the Vampiir test at 0x42CE1B,
# otherwise to the ordinary run/walk/back choice at 0x42CE3D. The hover-only class (flag 2) uses a threshold of 0.
HOVER_SPEED = 0x42CE14, '3dfa0000007e22', 0x42CE1B, 0x42CE3D
VAMPIIR_NOT_TINT = 0x4176E1

# Creation screen (vtable 0x95AEE8, constructor 0x59FC9B, allocated at 0x5999DD): its 16 class-button
# pointers live at +0xD8. Grow the object by 0x50 and move the array to a 20-slot array at +0x13C; every
# access uses a 32-bit displacement, so each is rewritten in place. Loop/range limits go from 16 to 20.
BUTTONS = 20
OLD_ARRAY, NEW_ARRAY = 0xD8, 0x13C
SCREEN_EDITS = [
    (0x5999DD, '683c010000', '688c010000'),          # operator new(0x13C) -> 0x18C
    (0x59FCFA, '8dbbd8000000', '8dbb3c010000'),      # ctor: lea edi,[ebx+array]
    (0x59FD1F, '837df010', '837df014'),              # ctor: create controls 1021+i for i < 20
    (0x59FF1D, '3d0d040000', '3d11040000'),          # click: control IDs 1021..1040
    (0x59FF36, '8b84bed8000000', '8b84be3c010000'),  # click: array[index]
    (0x59FF50, '8d86d8000000', '8d863c010000'),      # click: deselect loop start
    (0x59FF79, '837dfc10', '837dfc14'),              # click: deselect loop count
    (0x5A010F, '83f810', '83f814'), (0x5A0114, '8b8481d8000000', '8b84813c010000'),  # enable(index)
    (0x5A0124, '83f810', '83f814'), (0x5A0129, '8b8481d8000000', '8b84813c010000'),  # disable(index)
    (0x5A0139, '83f810', '83f814'), (0x5A013E, '8b8c81d8000000', '8b8c813c010000'),  # show(index)
    (0x5A0153, '83f810', '83f814'), (0x5A0158, '8b8c81d8000000', '8b8c813c010000'),  # hide(index)
    (0x5A0171, '8db7d8000000', '8db73c010000'),      # select(index): loop start
    (0x5A0196, '83fb10', '83fb14'),                  # select(index): loop count
    (0x599BE1, '83fa10', '83fa14'),                  # refresh: disable every class button
    (0x59E2AA, '83fa10', '83fa14'),                  # realm change: disable every class button
    (0x59ED47, '83fe10', '83fe14'),                  # race change: hide every class button
]
# The screen finds each button's label through a named UI adapter ("class_N_text"). The adapter manager's
# constructor registers class_0..class_15 in a loop (0x5ADC85, slots at +0x148); register class_16..19 right
# after it, with their adapter-pointer slots in this patch's own (writable) section.
ADAPTER_HOOK, ADAPTER_ORIGINAL, ADAPTER_BACK = 0x5ADCB3, '6880a79500', 0x5ADCB8
REGISTER_ADAPTER = 0x4B701A   # (owner, &slot, name) -> stores the new adapter in *slot; ret 0xC
SLOT_STORAGE = 0xC00          # section offset of the extra adapter-pointer slots
LAYOUT_SUPPORTED = '46d5de87dd1c4b2b3b1b1df3199556a0780ffe38d09f77ef41df7c4a9eda4a1c'

REG = {'eax': 0, 'ecx': 1, 'edx': 2, 'ebx': 3, 'esp': 4, 'ebp': 5, 'esi': 6, 'edi': 7}


def align(n, boundary):
    return (n + boundary - 1) // boundary * boundary


class Asm:
    """Minimal x86 emitter for the few instruction forms these stubs use."""

    def __init__(self, va):
        self.va, self.code = va, bytearray()

    @property
    def here(self):
        return self.va + len(self.code)

    def raw(self, hexbytes):
        self.code += bytes.fromhex(hexbytes)

    def rel(self, opcode, target):
        self.code += opcode + struct.pack('<i', target - (self.here + len(opcode) + 4))

    def call(self, target):
        self.rel(b'\xe8', target)

    def jmp(self, target):
        self.rel(b'\xe9', target)

    def ja(self, target):
        self.rel(b'\x0f\x87', target)

    def mov_frame(self, disp, value):
        """mov dword [ebp+disp], imm32 or register."""
        if isinstance(value, str):
            self.code += bytes([0x89, 0x45 | REG[value] << 3, disp & 0xff])
        else:
            self.code += bytes([0xc7, 0x45, disp & 0xff]) + struct.pack('<I', value)

    def mov_frame_byte(self, disp, value):
        if isinstance(value, str):  # low byte of a register (bl for ebx)
            self.code += bytes([0x88, 0x45 | REG[value] << 3, disp & 0xff])
        else:
            self.code += bytes([0xc6, 0x45, disp & 0xff, value])

    def push(self, value):
        if isinstance(value, str):
            self.code += bytes([0x50 + REG[value]])
        else:
            self.code += bytes([0x6a, value])


def creation_entry(asm, realm, slot, name_va, description_va):
    """One class registration, built like the realm's Mauler entry. Registers ebx/edi survive the calls."""
    asm.mov_frame(-0x3c, 0)
    asm.mov_frame(-0x20, 0)
    asm.mov_frame(-0x50, 0)            # resource 0: the client falls back to the supplied text
    asm.mov_frame(-0x4c, name_va)
    asm.mov_frame(-0x48, 4)
    asm.mov_frame_byte(-0x44, realm['flags'])
    asm.mov_frame(-0x40, realm['flags'])
    asm.mov_frame(-0x38, 0)
    asm.mov_frame(-0x34, description_va)
    asm.mov_frame(-0x1c, realm['flags'])
    asm.mov_frame(-0x18, realm['field18'])
    for disp in (-0x10, -0x0f, -0x0e):  # the three creation ratings the Mauler entry uses
        asm.mov_frame_byte(disp, 0x0a)
    for race in slot.races:
        asm.mov_frame(-0x14, race)
        asm.raw('8d45ec 50 8d45d0')      # push &race; eax = &list
        asm.call(ADD_RACE)
    asm.push(realm['count'])
    asm.raw('83ec10 8bc4 8965ec 50 8d4dd0')  # by-value list argument
    asm.call(COPY_LIST)
    asm.raw('8d45f0 50 8d45e0 50')
    asm.push(realm['realm'])
    asm.push(slot.class_id)
    asm.raw('ff7508 8d4db0')
    asm.call(REGISTER_CLASS)
    asm.raw('8d75d0')                  # esi = &list
    asm.call(CLEAR_LIST)


def creation_stub(asm, realm, text):
    for slot in realm['slots']:
        creation_entry(asm, realm, slot, text[slot.class_id, 'name'], text[slot.class_id, 'description'])
    asm.raw(realm['original'])
    asm.jmp(realm['back'])


def name_stub(asm, names):
    asm.raw('56 57')
    for slot, va in names:
        size = len(slot.name) + 1
        if size > NAME_SLOT:
            raise ValueError('Class name too long: ' + slot.name)
        asm.raw('be' + struct.pack('<I', va).hex() + 'bf' + struct.pack('<I', NAME_TABLE + NAME_SLOT * slot.class_id).hex())
        asm.raw('b9' + struct.pack('<I', size).hex() + 'f3a4')  # rep movsb
    asm.raw('5f 5e')
    asm.raw(NAME_ORIGINAL)
    asm.jmp(NAME_BACK)


def stat_stub(asm):
    for slot in SLOTS:
        primary, secondary, tertiary = slot.stats
        case = bytes.fromhex('c701') + struct.pack('<I', primary) + bytes.fromhex('c74104') + struct.pack('<I', secondary)
        case += bytes.fromhex('c74108') + struct.pack('<I', tertiary) + bytes.fromhex('31c040c3')  # eax = 1, ret
        asm.raw('83fa' + bytes([slot.class_id]).hex() + '75' + bytes([len(case)]).hex())
        asm.code += case
    asm.raw('4a 83fa3d')
    asm.ja(STAT_DEFAULT)
    asm.jmp(STAT_CASES)


def adapter_stub(asm, names, slots_va):
    asm.raw('53 57')                   # keep the constructor's ebx/edi
    for index, name_va in enumerate(names):
        asm.raw('68' + struct.pack('<I', name_va).hex())
        asm.raw('68' + struct.pack('<I', slots_va + 4*index).hex())
        asm.raw('56')                  # owner (the adapter manager being constructed)
        asm.call(REGISTER_ADAPTER)
    asm.raw('5f 5b')
    asm.raw(ADAPTER_ORIGINAL)
    asm.jmp(ADAPTER_BACK)


def hover_select_stub(asm):
    # al = 1 for a Vampiir, 2 for the hover-only class, else 0.
    asm.raw('8a461d 3c3a 7408 3c' + bytes([HOVER_CLASS]).hex() + ' 7408 30c0 eb06 b001 eb02 b002')
    asm.jmp(HOVER_SELECT[2])


def hover_world_stub(asm):
    # The original compare against 58 (0x417F9B) still sets the flags for this sete.
    asm.raw('0f94c0 807b49' + bytes([HOVER_CLASS]).hex() + ' 7502 b002')
    asm.raw('807dc001')                # the replaced compare; its flags reach the jne at 0x417FE1
    asm.jmp(HOVER_WORLD[2])


def vampiir_only_stub(asm, original, back, kind):
    if kind in ('cl', 'al'):
        reg = {'cl': 'c9', 'al': 'c0'}[kind]
        asm.raw(original)                                   # mov reg,[obj+0x19c4]
        asm.raw(('80f902' if kind == 'cl' else '3c02') + ' 7502 30' + reg)  # 2 (hover only) -> 0
    elif kind == 'cmp_je':
        asm.raw('80bec419000001')                           # cmp byte [esi+0x19c4],1
        asm.rel(bytes.fromhex('0f85'), VAMPIIR_NOT_TINT)
    elif kind == 'setne':
        asm.raw('80b8c419000001 0f94c1 84c9')               # ZF clear only for a real Vampiir
    elif kind == 'model_setne':
        asm.raw('8945e8')                                   # the moved mov [ebp-0x18],eax
        asm.raw('80bec419000001 0f94c1')                    # cl = 1 only for a real Vampiir
    elif kind == 'ui_jne':
        asm.raw('80b8c419000001')                           # cmp byte [eax+0x19c4],1
        asm.rel(bytes.fromhex('0f84'), VAMPIIR_UI_SKIP[back - 9])
    asm.jmp(back)


def hover_speed_stub(asm):
    site, original, vampiir_test, ordinary = HOVER_SPEED
    asm.raw('80bbc419000002 750d')                          # cmp byte [ebx+0x19c4],2; jne stock
    asm.raw('85c0')                                         # hover-only class: any forward speed
    asm.rel(bytes.fromhex('0f8e'), ordinary)
    asm.jmp(vampiir_test)
    asm.raw('3dfa000000')                                   # stock: speed over 250
    asm.rel(bytes.fromhex('0f8e'), ordinary)
    asm.jmp(vampiir_test)


def flight_stub(asm):
    flag = struct.pack('<I', FLIGHT_FLAG).hex()
    asm.raw('9c')                                                     # keep flags
    asm.raw('807b0101 750a c705' + flag + '01000000')                 # byte 1 == 1: fly
    asm.raw('807b0102 750a c705' + flag + '00000000')                 # byte 1 == 2: land
    asm.raw('9d')
    asm.raw(FLIGHT_ORIGINAL)
    asm.jmp(FLIGHT_BACK)


def flight_anim_stub(asm, original, back, kind, zero_va):
    flag = struct.pack('<I', FLIGHT_FLAG).hex()
    zero = struct.pack('<I', zero_va).hex()
    test = '833d' + flag + '00'                                       # cmp dword [flag],0
    if kind == 'player':
        asm.raw(test + ' 7518')                                       # jne fly (past the 24-byte normal path)
        asm.raw(original[:-4])                                        # the original reads, without its jmp
        asm.jmp(back)
        asm.raw('c74508 00000000 31c0')                               # fly: speed 0, keys 0
    elif kind == 'mode':
        asm.raw(original + test + ' 7407 31c0 be' + zero)             # flying: mode 0, esi = zeroed struct
    elif kind == 'keys':
        asm.raw(original + test + ' 7402 31db')                       # flying: no turn keys
    elif kind == 'mount':
        asm.raw(original + test + ' 7405 bb' + zero)                  # flying: ebx = zeroed struct
    asm.jmp(back)


def resist_stub(asm):
    # Codex's Storm Sovereign fix (patch_modern_client.py): the resist label includes secondary RA
    # resistance with ResistCalculator's ordering, capped at 70.
    asm.raw('5052 0fbf4fee 29ca b964000000 29d1 0fb6563f 0fafca 89c8 99 b964000000 f7f9 5a 01c2'
            '83fa46 7e05 ba46000000 58 89904c93d400')
    asm.jmp(RESIST_BACK)


def patch(original):
    if hashlib.sha256(original).hexdigest() != SUPPORTED:
        raise ValueError('Unsupported 1.130 game.dll SHA256; patch only the pristine client')
    data = bytearray(original)
    pe = struct.unpack_from('<I', data, 60)[0]
    if data[pe:pe+4] != b'PE\0\0' or struct.unpack_from('<H', data, pe+4)[0] != 0x14c:
        raise ValueError('Expected x86 PE')
    opt = pe + 24
    if struct.unpack_from('<I', data, opt+28)[0] != BASE:
        raise ValueError('Unexpected image base')
    count = struct.unpack_from('<H', data, pe+6)[0]
    table = opt + struct.unpack_from('<H', data, pe+20)[0]
    sections = [struct.unpack_from('<8sIIIIIIHHI', data, table+40*i) for i in range(count)]
    if table+(count+1)*40 > min(s[4] for s in sections if s[4]):
        raise ValueError('No section header space')

    def offset(va):
        for s in sections:
            if s[2] <= va-BASE < s[2]+s[3]:
                return s[4]+va-BASE-s[2]
        raise ValueError(f'Address not backed by file: {va:x}')

    def hook(va, original_hex, target):
        before = bytes.fromhex(original_hex)
        pos = offset(va)
        if data[pos:pos+len(before)] != before:
            raise ValueError(f'Unexpected instruction at {va:x}')
        jump = b'\xe9'+struct.pack('<i', target-(va+5))
        data[pos:pos+len(before)] = jump + b'\x90'*(len(before)-5)

    fa = struct.unpack_from('<I', data, opt+36)[0]
    sa = struct.unpack_from('<I', data, opt+32)[0]
    raw = align(len(data), fa)
    rva = align(max(s[2]+max(s[1], s[3]) for s in sections), sa)
    va = BASE + rva

    # Strings first (fixed offset), then code; every stub knows its own address.
    strings = bytearray()
    text = {}
    for slot in SLOTS:
        for kind, value in (('name', slot.name), ('description', slot.description)):
            text[slot.class_id, kind] = va + 0x800 + len(strings)
            strings += value.encode('latin-1') + b'\0'
    asm = Asm(va)
    stubs = {}
    stubs['name'] = asm.here
    name_stub(asm, [(slot, text[slot.class_id, 'name']) for slot in SLOTS])
    stubs['stats'] = asm.here
    stat_stub(asm)
    stubs['resist'] = asm.here
    resist_stub(asm)
    if TUATHA_HOVER:
        stubs['hover_select'] = asm.here
        hover_select_stub(asm)
        stubs['hover_world'] = asm.here
        hover_world_stub(asm)
        for site, original, back, kind in VAMPIIR_ONLY:
            stubs[f'vampiir_{site:x}'] = asm.here
            vampiir_only_stub(asm, original, back, kind)
        stubs['hover_speed'] = asm.here
        hover_speed_stub(asm)
    adapter_names = []
    for index in range(16, BUTTONS):
        adapter_names.append(va + 0x800 + len(strings))
        strings += f'class_{index}_text'.encode('latin-1') + b'\0'
    stubs['adapters'] = asm.here
    adapter_stub(asm, adapter_names, va + SLOT_STORAGE)
    for realm_name, realm in REALMS.items():
        stubs[realm_name] = asm.here
        creation_stub(asm, realm, text)
    stubs['flight'] = asm.here         # last, so the earlier stubs keep their addresses
    flight_stub(asm)
    for site, original, back, kind in FLIGHT_ANIM_HOOKS:
        stubs[f'flight_anim_{site:x}'] = asm.here
        flight_anim_stub(asm, original, back, kind, va + FLIGHT_ZERO)
    if len(asm.code) > 0x800:
        raise ValueError('Stubs exceed the reserved code area')
    payload = bytes(asm.code).ljust(0x800, b'\xcc') + bytes(strings)
    if len(payload) > SLOT_STORAGE:
        raise ValueError('Strings overlap the adapter slot storage')
    payload = payload.ljust(SLOT_STORAGE, b'\0') + b'\0' * (4 * (BUTTONS - 16))
    if len(payload) > FLIGHT_ZERO:
        raise ValueError('Adapter slots overlap the flight zero block')
    payload = payload.ljust(FLIGHT_ZERO + 0x40, b'\0')

    for at, before, after in [CLASS_ARRAY_SIZE, RIDE_ANIM_EDIT]+SCREEN_EDITS+CAMERA_MAX_EDITS+FLIGHT_SPEED_EDITS:
        old, new = bytes.fromhex(before), bytes.fromhex(after)
        pos = offset(at)
        if len(old) != len(new) or data[pos:pos+len(old)] != old:
            raise ValueError(f'Unexpected instruction at {at:x}')
        data[pos:pos+len(new)] = new
    hook(NAME_HOOK, NAME_ORIGINAL, stubs['name'])
    hook(STAT_HOOK, STAT_ORIGINAL, stubs['stats'])
    hook(RESIST_HOOK, RESIST_ORIGINAL, stubs['resist'])
    hook(FLIGHT_HOOK, FLIGHT_ORIGINAL, stubs['flight'])
    for site, original, back, kind in FLIGHT_ANIM_HOOKS:
        hook(site, original, stubs[f'flight_anim_{site:x}'])
    if TUATHA_HOVER:
        hook(HOVER_SELECT[0], HOVER_SELECT[1], stubs['hover_select'])
        hook(HOVER_WORLD[0], HOVER_WORLD[1], stubs['hover_world'])
        for site, original, back, kind in VAMPIIR_ONLY:
            hook(site, original, stubs[f'vampiir_{site:x}'])
        hook(HOVER_SPEED[0], HOVER_SPEED[1], stubs['hover_speed'])
    hook(ADAPTER_HOOK, ADAPTER_ORIGINAL, stubs['adapters'])
    for realm_name, realm in REALMS.items():
        hook(realm['hook'], realm['original'], stubs[realm_name])

    raw_size = align(len(payload), fa)
    data.extend(b'\0'*(raw-len(data)))
    data.extend(payload.ljust(raw_size, b'\0'))
    struct.pack_into('<8sIIIIIIHHI', data, table+count*40, b'.slots\0\0', len(payload), rva, raw_size, raw, 0, 0, 0, 0, 0xE0000020)  # code, read, write: holds adapter slots
    struct.pack_into('<H', data, pe+6, count+1)
    struct.pack_into('<I', data, opt+56, align(rva+len(payload), sa))
    struct.pack_into('<I', data, opt+4, struct.unpack_from('<I', data, opt+4)[0]+raw_size)
    struct.pack_into('<I', data, opt+64, 0)
    checksum = 0
    for pos in range(0, len(data), 2):
        checksum += int.from_bytes(data[pos:pos+2], 'little')
        checksum = (checksum & 0xffff)+(checksum >> 16)
    checksum = (checksum & 0xffff)+(checksum >> 16)
    struct.pack_into('<I', data, opt+64, checksum+len(data))
    return bytes(data), dict(section_va=va, stubs=stubs, text=text)


def patch_layout(original):
    """Add class buttons class_16..class_19 (controls 1037-1040) to pregame/character_creation.xml."""
    if hashlib.sha256(original).hexdigest() != LAYOUT_SUPPORTED:
        raise ValueError('Unsupported character_creation.xml; patch only the stock 1.130 layout')
    text = original.decode('latin-1')
    newline = '\r\n' if '\r\n' in text else '\n'
    anchor = '<Adapter>class_15_text</Adapter>'
    end = text.index('</ButtonDef>', text.index(anchor)) + len('</ButtonDef>')
    start = text.rindex('<ButtonDef>', 0, text.index(anchor))
    template = text[start:end]
    indent = text[text.rindex(newline, 0, start)+len(newline):start]
    added = ''
    for index in range(16, BUTTONS):
        block = template.replace('<ControlId>1036</ControlId>', f'<ControlId>{1021+index}</ControlId>')
        block = block.replace('<Label>Class 15</Label>', f'<Label>Class {index}</Label>')
        block = block.replace(anchor, f'<Adapter>class_{index}_text</Adapter>')
        x = 795 if index % 2 == 0 else 901
        y = 378 + 25*(index//2)  # same two-column grid as class_0..class_15
        block = block.replace('<X>901</X>', f'<X>{x}</X>').replace('<Y>553</Y>', f'<Y>{y}</Y>')
        added += newline + indent + block
    return (text[:end] + added + text[end:]).encode('latin-1')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('client', type=Path, help='pristine 1.130 game.dll')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    result, layout = patch(args.client.read_bytes())
    if args.output:
        with args.output.open('xb') as stream:
            stream.write(result)
    print('Patched SHA256:', hashlib.sha256(result).hexdigest())
    print('Section .slots at', hex(layout['section_va']), {k: hex(v) for k, v in layout['stubs'].items()})
    print('Written to '+str(args.output) if args.output else 'Preview only')
