"""Optional verification (needs pefile and unicorn): emulate the name and stat stubs of a patched game.dll.
Usage: python verify_stub_emulation.py <patched game.dll>"""
import sys,struct,pefile
from unicorn import Uc,UC_ARCH_X86,UC_MODE_32,UcError
from unicorn.x86_const import *
pe=pefile.PE(sys.argv[1]);img=pe.get_memory_mapped_image();B=pe.OPTIONAL_HEADER.ImageBase
def machine():
    mu=Uc(UC_ARCH_X86,UC_MODE_32);size=(len(img)+0xfff)&~0xfff
    mu.mem_map(B,size);mu.mem_write(B,img)
    mu.mem_map(0x100000,0x10000);mu.reg_write(UC_X86_REG_ESP,0x10f000)
    return mu
def run(mu,start,stop):
    mu.emu_start(start,stop,count=2000)
ok=True
sys.path.insert(0,__import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import patch_class_slots as P
# name stub: run from the hooked instruction to the return target
mu=machine()
for r,v in [(UC_X86_REG_ESI,0x11111111),(UC_X86_REG_EDI,0x22222222),(UC_X86_REG_EAX,0x33333333),(UC_X86_REG_EDX,0x44444444),(UC_X86_REG_EBX,0x55555555)]:mu.reg_write(r,v)
esp=mu.reg_read(UC_X86_REG_ESP)
run(mu,0x44efd5,0x44efdb)
names={c:mu.mem_read(0xd92578+30*c,30).split(b'\0')[0].decode() for c in (62,63,64,65,66,67)}
regs=[mu.reg_read(r) for r in (UC_X86_REG_ESI,UC_X86_REG_EDI,UC_X86_REG_EAX,UC_X86_REG_EDX,UC_X86_REG_EBX,UC_X86_REG_ESP)]
ecx=mu.reg_read(UC_X86_REG_ECX);expect_ecx=struct.unpack('<I',bytes(mu.mem_read(0xd93264,4)))[0]
print('names',names,'regs preserved',regs[:5]==[0x11111111,0x22222222,0x33333333,0x44444444,0x55555555] and regs[5]==esp,'ecx=string manager',ecx==expect_ecx)
ok&=names[63]=='Sluaghbinder' and names[64]=='Storm Sovereign' and names[65]=='Pendragon Sovereign' and names[66]=='Death Knight' and names[67]=='Tuatha Sovereign' and regs[5]==esp
# stat function: fastcall-like (ECX=out, EDX=class), returns AL
def stats(cls):
    mu=machine();ret=0x100100
    mu.mem_write(0x100100,b'\xf4')  # hlt
    esp=0x10f000-4;mu.mem_write(esp,struct.pack('<I',ret));mu.reg_write(UC_X86_REG_ESP,esp)
    out=0x101000;mu.mem_write(out,b'\xee'*12)
    mu.reg_write(UC_X86_REG_ECX,out);mu.reg_write(UC_X86_REG_EDX,cls)
    run(mu,0x5b400a,ret)
    vals=struct.unpack('<3I',bytes(mu.mem_read(out,12)))
    return mu.reg_read(UC_X86_REG_EAX)&0xff,vals,mu.reg_read(UC_X86_REG_ESP)==0x10f000
for cls in (63,64,65,66,67,60,1,14):
    al,vals,balanced=stats(cls)
    print('class',cls,'al',al,'stats',[hex(v) for v in vals],'stack ok',balanced)
    ok&=balanced
exp={63:(1,(0,4,1)),64:(1,(0,5,1)),65:(1,(0,5,1)),66:(1,(4,2,3)),67:(1,(0,5,1)),60:(1,(0,1,3)),1:(1,(1,5,0))}
for c,(a,v) in exp.items():
    al,vals,_=stats(c);ok&=(al==a and vals==v)
al,vals,_=stats(14);ok&=(al==0)
# Vampiir byte (+0x15D): 1 for class 58 (full Vampiir), 2 for class 67 (hover only), 0 otherwise.
def hover(cls):
    mu=machine();esi,ebx,ebp=0x104000,0x106000,0x10a000
    for r,v in [(UC_X86_REG_ESI,esi),(UC_X86_REG_EBX,ebx),(UC_X86_REG_EBP,ebp)]:mu.reg_write(r,v)
    mu.mem_write(esi+0x1d,bytes([cls]));mu.reg_write(UC_X86_REG_EAX,0x77)
    run(mu,0x402a66,0x402a73);select=mu.mem_read(ebx+0x15d,1)[0]
    mu=machine()
    for r,v in [(UC_X86_REG_ESI,esi),(UC_X86_REG_EBX,ebx),(UC_X86_REG_EBP,ebp)]:mu.reg_write(r,v)
    mu.mem_write(ebx+0x49,bytes([cls]));mu.mem_write(ebp+0x5b,bytes([0x5a]));mu.mem_write(ebp-0x40,bytes([0]))
    run(mu,0x417f98,0x417fe1);world=mu.mem_read(ebx+0x15d,1)[0];kept=bytes(mu.mem_read(ebx+0x188,2))==bytes([0x5a,0x5a])
    return select,world,kept
for cls,expect in ((58,1),(67,2 if P.TUATHA_HOVER else 0),(66,0),(63,0)):
    s,w,k=hover(cls);print('hover class',cls,'select',s,'world',w,'al kept',k);ok&=(s==expect and w==expect and k)
# Skin/model readers of the world flag (+0x19C4) treat only 1 as Vampiir; 2 reads as 0 there.
sys.path.insert(0,__import__('os').path.dirname(__import__('os').path.abspath(__file__)))
import patch_class_slots as P
from unicorn import UC_HOOK_CODE
def reader(site,back,kind,flag):
    mu=machine();obj=0x104000
    for r in (UC_X86_REG_ESI,UC_X86_REG_EAX):mu.reg_write(r,obj)
    mu.reg_write(UC_X86_REG_EBX,0);mu.reg_write(UC_X86_REG_ECX,0);mu.reg_write(UC_X86_REG_EBP,0x10a000);mu.mem_write(obj+0x19c4,bytes([flag]))
    if kind=='ui_jne':
        skip=P.VAMPIIR_UI_SKIP[site];hit=[]
        def h(mu,addr,size,_):
            if addr in (back,skip):hit.append(addr);mu.emu_stop()
        mu.hook_add(UC_HOOK_CODE,h);mu.emu_start(site,0,count=50)
        return 1 if hit==[skip] else 0
    if kind=='cmp_je':
        stops=(back,P.VAMPIIR_NOT_TINT);hit=[]
        def h(mu,addr,size,_):
            if addr in stops:hit.append(addr);mu.emu_stop()
        mu.hook_add(UC_HOOK_CODE,h);mu.emu_start(site,0,count=50)
        return 1 if hit==[back] else 0
    if kind=='setne':
        run(mu,site,0x45014b);return mu.reg_read(UC_X86_REG_ECX)&0xff
    run(mu,site,back)
    return mu.reg_read(UC_X86_REG_ECX if kind in ('cl','model_setne') else UC_X86_REG_EAX)&0xff
for site,original,back,kind in (P.VAMPIIR_ONLY if P.TUATHA_HOVER else []):
    got=[reader(site,back,kind,f) for f in (0,1,2)]
    good=got==[0,1,0];print('vampiir-only reader',hex(site),kind,'flag 0/1/2 ->',got,'ok',good);ok&=good
# Hover-move animation: the hover-only class (flag 2) takes it at any forward speed, a Vampiir still only over 250.
def speed(flag,spd):
    mu=machine();obj=0x104000;mu.reg_write(UC_X86_REG_EBX,obj);mu.reg_write(UC_X86_REG_EAX,spd);mu.mem_write(obj+0x19c4,bytes([flag]))
    site,_,vamp,ordinary=P.HOVER_SPEED;hit=[]
    def h(mu,addr,size,_):
        if addr in (vamp,ordinary):hit.append(addr);mu.emu_stop()
    mu.hook_add(UC_HOOK_CODE,h);mu.emu_start(site,0,count=50)
    return 'hover' if hit==[vamp] else 'ordinary'
if P.TUATHA_HOVER:
    for flag,spd,want in ((2,40,'hover'),(2,191,'hover'),(2,0,'ordinary'),(2,-50,'ordinary'),(1,191,'ordinary'),(1,300,'hover'),(0,191,'ordinary')):
        got=speed(flag,spd);good=got==want;print('hover speed flag',flag,'speed',spd,'->',got,'ok',good);ok&=good
print('ALL OK' if ok else 'FAILED')
