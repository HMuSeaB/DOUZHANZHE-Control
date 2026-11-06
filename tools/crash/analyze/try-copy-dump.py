# -*- coding: utf-8 -*-
"""尝试直接把新的 Minidump 复制到工作区（只读尝试，不改动系统设置）。"""
import os, glob, shutil

SRC = r'C:\Windows\Minidump'
DST = r'C:\Users\36230\WorkBuddy\2026-11-04-22-45-13\dumps'

os.makedirs(DST, exist_ok=True)
for p in sorted(glob.glob(os.path.join(SRC, '*.dmp'))):
    name = os.path.basename(p)
    out = os.path.join(DST, name)
    if os.path.exists(out) and os.path.getsize(out) > 0:
        print('已有:', name)
        continue
    try:
        with open(p, 'rb') as f:
            head = f.read(8)
        print('可读:', name, '头8字节=', head.hex())
        shutil.copy2(p, out)
        print('  已复制 ->', out, os.path.getsize(out), 'bytes')
    except PermissionError as e:
        print('权限不足:', name, e)
    except Exception as e:
        print('失败:', name, type(e).__name__, e)

print()
print('--- dumps 目录现状 ---')
for p in sorted(glob.glob(os.path.join(DST, '*.dmp'))):
    print(' ', os.path.basename(p), os.path.getsize(p))
