import struct, glob, os, datetime, sys

def u32(b,o): return struct.unpack_from('<I',b,o)[0]
def u64(b,o): return struct.unpack_from('<Q',b,o)[0]

for f in sorted(glob.glob(r'C:/Users/36230/WorkBuddy/2026-11-04-22-45-13/dumps/*.dmp')):
    b=open(f,'rb').read()
    print('='*70)
    print(os.path.basename(f), 'size=0x%X'%len(b))
    print(' sig      ', b[0:4], b[4:8])
    print(' major/min', u32(b,0x08), u32(b,0x0C))
    print(' procs    ', u32(b,0x34))
    bc = u32(b,0x38)
    p1,p2,p3,p4 = u64(b,0x40),u64(b,0x48),u64(b,0x50),u64(b,0x58)
    print(' BugCheck  : 0x%X'%bc)
    print(' params    : 0x%016X 0x%016X 0x%016X 0x%016X'%(p1,p2,p3,p4))
    st = u64(b,0x1F08)
    try:
        print(' systemtime: ', datetime.datetime(1601,1,1)+datetime.timedelta(microseconds=st//10))
    except Exception as e: pass
    print(' DumpType  : 0x%X'%u32(b,0x1F00) if len(b)>0x1F00 else '')
    cm = b[0x1F10:0x1F90].decode('utf-16le','ignore').strip('\x00')
    print(' comment   :', cm[:120])
    # triage at 0x2000
    t=0x2000
    print(' triage svc=%d size=0x%X valid=0x%X ctx=0x%X exc=0x%X mm=0x%X unld=0x%X prcb=0x%X proc=0x%X thr=0x%X'%(
        u32(b,t),u32(b,t+4),u32(b,t+8),u32(b,t+0xC),u32(b,t+0x10),u32(b,t+0x14),u32(b,t+0x18),u32(b,t+0x1C),u32(b,t+0x20),u32(b,t+0x24)))
    print(' callstack=0x%X size=0x%X drivers=0x%X count=%d strpool=0x%X strsize=0x%X broken=0x%X opts=0x%X top=0x%X'%(
        u32(b,t+0x28),u32(b,t+0x2C),u32(b,t+0x30),u32(b,t+0x34),u32(b,t+0x38),u32(b,t+0x3C),u32(b,t+0x40),u32(b,t+0x44),u64(b,t+0x48)))
