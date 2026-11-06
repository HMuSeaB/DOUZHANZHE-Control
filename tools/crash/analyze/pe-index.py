import struct, os, glob, json

def soi(path):
    try:
        with open(path, 'rb') as fh:
            h = fh.read(0x400)
        if h[:2] != b'MZ':
            return None
        e = struct.unpack_from('<I', h, 0x3C)[0]
        with open(path, 'rb') as fh:
            fh.seek(e)
            pe = fh.read(0x180)
        if pe[:4] != b'PE\0\0':
            return None
        return struct.unpack_from('<I', pe, 24 + 0x38)[0]
    except Exception:
        return None

roots = [
    r'C:\Windows\System32\drivers',
    r'C:\Windows\System32',
    r'C:\Program Files\AntiCheatExpert',
    r'C:\Program Files\ldplayer9box',
    r'C:\Program Files (x86)\AntiCheatExpert',
    r'C:\Program Files\ACE',
]
index = {}
count = 0
for r in roots:
    if not os.path.isdir(r):
        continue
    for ext in ('*.sys', '*.dll', '*.exe'):
        for p in glob.glob(os.path.join(r, ext)):
            s = soi(p)
            if s:
                index.setdefault(s, []).append(p)
                count += 1

out = r'C:\Users\36230\WorkBuddy\2026-11-04-22-45-13\tools\pe_sizes2.json'
json.dump(index, open(out, 'w'))
print('files indexed:', count, ' distinct sizes:', len(index))
for r in roots:
    print('  ', r, 'exists' if os.path.isdir(r) else 'MISSING')
print()
print('ACE 相关文件:')
for s, v in index.items():
    for p in v:
        if 'anticheat' in p.lower() or os.path.basename(p).lower().startswith('ace'):
            print('   %08X  %s' % (s, p))
print()
print('ldplayer9box 文件:')
for s, v in index.items():
    for p in v:
        if 'ldplayer9box' in p.lower():
            print('   %08X  %s' % (s, os.path.basename(p)))
