# -*- coding: utf-8 -*-
"""只读复核：脚本编码/内容 + 当前系统状态。不修改任何系统设置。"""
import os, glob, ctypes, datetime, winreg

BASE = r'C:\Users\36230\WorkBuddy\2026-11-04-22-45-13'

print('=== A. 脚本内容与编码校验 ===')
names = [
    '3-开启驱动验证器(特殊池).bat',
    '4-关闭驱动验证器.bat',
    '1-禁用雷电内核驱动.bat',
    '2-恢复雷电内核驱动.bat',
    '清理雷电残留安装记录.bat',
    '复制蓝屏转储.bat',
]
for name in names:
    p = os.path.join(BASE, name)
    if not os.path.exists(p):
        print('  缺失:', name)
        continue
    raw = open(p, 'rb').read()
    try:
        txt = raw.decode('gb18030')
        enc = 'GB18030 OK'
    except UnicodeDecodeError:
        txt = raw.decode('gb18030', errors='replace')
        enc = 'GB18030 有坏字节'
    crlf = (raw.count(b'\r\n') == raw.count(b'\n'))
    bom = raw.startswith(b'\xef\xbb\xbf')
    print('--- %s | %d bytes | %s | CRLF一致=%s | BOM=%s' % (name, len(raw), enc, crlf, bom))
    if name.startswith('3-') or name.startswith('4-'):
        for line in txt.splitlines():
            s = line.strip()
            if s.lower().startswith('verifier') or s.startswith('chcp') or s.startswith('pause') or s.startswith('echo ') or s.startswith('REM '):
                print('      ', s)

print()
print('=== B. 当前 Driver Verifier 注册表状态 ===')
mm = r'SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management'
try:
    k = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, mm)
    for n in ('VerifyDrivers', 'VerifyDriverLevel', 'VerifyFlags',
              'VerifyBootMode', 'VerifyDriverBootMode', 'PoolTagOverruns'):
        try:
            print('  %-22s = %r' % (n, winreg.QueryValueEx(k, n)[0]))
        except FileNotFoundError:
            print('  %-22s = (无)' % n)
    winreg.CloseKey(k)
except Exception as e:
    print('  读取失败:', e)

print()
print('=== C. 本次开机时间（GetTickCount64 推算）===')
up = ctypes.windll.kernel32.GetTickCount64() / 1000.0
boot = datetime.datetime.now() - datetime.timedelta(seconds=up)
print('  现在       =', datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S'))
print('  推算开机   =', boot.strftime('%Y-%m-%d %H:%M:%S'))
print('  已运行     = %.1f 分钟' % (up / 60))

print()
print('=== D. Minidump 目录（有无今天新增）===')
try:
    fs = sorted(glob.glob(r'C:\Windows\Minidump\*.dmp'), key=os.path.getmtime)
    for p in fs:
        st = os.stat(p)
        print('  %-28s %s  %d bytes' % (
            os.path.basename(p),
            datetime.datetime.fromtimestamp(st.st_mtime).strftime('%Y-%m-%d %H:%M:%S'),
            st.st_size))
    if not fs:
        print('  (空)')
except Exception as e:
    print('  读取失败:', e)

print()
print('=== E. 相关内核服务启动类型 ===')
svc = r'SYSTEM\CurrentControlSet\Services'
for name in ('ACE-BASE', 'ACE-BOOT', 'ACE-ADVT', 'ACE-CORE30405', 'ACE-GAME',
             'Ld9BoxNetLwf', 'Ld9BoxSup', 'BAPIDRV64', 'lnvscenter',
             'XunYouFilter', 'netfilter2', 'SangforVnic', 'npcap',
             'wintun', 'ViGEmBus', 'PawnIO', 'uuycinput', 'NeSigVerify'):
    try:
        k2 = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, svc + '\\' + name)
        try:
            start = winreg.QueryValueEx(k2, 'Start')[0]
        except FileNotFoundError:
            start = '?'
        try:
            img = winreg.QueryValueEx(k2, 'ImagePath')[0]
        except FileNotFoundError:
            img = ''
        print('  %-16s Start=%-4s %s' % (name, start, img))
        winreg.CloseKey(k2)
    except FileNotFoundError:
        print('  %-16s (服务键不存在)' % name)
    except Exception as e:
        print('  %-16s 读取失败: %s' % (name, e.__class__.__name__))

print()
print('=== F. ACLOS / 无畏契约日志目录 ===')
for d in (r'D:\ACLOS\Cross\Log', r'D:\ACLOS'):
    try:
        items = [(p, os.path.getmtime(p)) for p in glob.glob(d + r'\*')]
        items.sort(key=lambda x: -x[1])
        print('  %s -> %d 项，最新 3 个:' % (d, len(items)))
        for p, t in items[:3]:
            print('     %s  %s' % (
                datetime.datetime.fromtimestamp(t).strftime('%Y-%m-%d %H:%M'),
                os.path.basename(p)))
    except Exception as e:
        print('  %s 读取失败: %s' % (d, e))
