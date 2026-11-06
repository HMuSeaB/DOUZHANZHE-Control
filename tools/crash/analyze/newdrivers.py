# -*- coding: utf-8 -*-
"""只读：找出 11/1 之后新增/改动的驱动文件、FlClash 安装信息、管家目录时间。"""
import os, glob, datetime, winreg

def ts(t):
    return datetime.datetime.fromtimestamp(t).strftime('%Y-%m-%d %H:%M')

CUT = datetime.datetime(2026, 11, 1).timestamp()

print('=== A. System32\\drivers 中 11/1 之后安装或有改动的 .sys ===')
rows = []
for p in glob.glob(r'C:\Windows\System32\drivers\*.sys'):
    try:
        st = os.stat(p)
    except OSError:
        continue
    newest = max(st.st_mtime, st.st_ctime)
    if newest >= CUT:
        rows.append((newest, p, st))
for newest, p, st in sorted(rows, reverse=True):
    print('  %-34s 创建=%s 修改=%s %d KB' % (
        os.path.basename(p), ts(st.st_ctime), ts(st.st_mtime), st.st_size // 1024))
if not rows:
    print('  (无)')

print()
print('=== B. 重点服务的驱动文件时间 ===')
svc = r'SYSTEM\CurrentControlSet\Services'
for name in ('Ld9BoxNetLwf', 'Ld9BoxSup', 'netfilter2', 'wintun', 'npcap',
             'SangforVnic', 'XunYouFilter', 'NeSigVerify', 'lnvscenter',
             'ViGEmBus', 'ACE-ADVT', 'ACE-CORE30405', 'ACE-GAME'):
    try:
        k = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, svc + '\\' + name)
        img = winreg.QueryValueEx(k, 'ImagePath')[0]
        winreg.CloseKey(k)
    except Exception:
        print('  %-16s (无服务键)' % name)
        continue
    img = img.lstrip('\\??\\')
    if img.lower().startswith('systemroot\\'):
        img = r'C:\Windows\\' + img[11:]
    elif img.lower().startswith('system32\\'):
        img = r'C:\Windows\\' + img
    if not os.path.exists(img):
        print('  %-16s 文件不存在: %s' % (name, img))
        continue
    st = os.stat(img)
    print('  %-16s 创建=%s 修改=%s  %s' % (name, ts(st.st_ctime), ts(st.st_mtime), img))

print()
print('=== C. FlClash 卸载项（11/4 安装）===')
for hive, base in ((winreg.HKEY_LOCAL_MACHINE, r'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'),
                   (winreg.HKEY_LOCAL_MACHINE, r'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'),
                   (winreg.HKEY_CURRENT_USER, r'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')):
    try:
        k = winreg.OpenKey(hive, base)
    except OSError:
        continue
    i = 0
    while True:
        try:
            sub = winreg.EnumKey(k, i)
        except OSError:
            break
        i += 1
        try:
            kk = winreg.OpenKey(k, sub)
            dn = winreg.QueryValueEx(kk, 'DisplayName')[0]
        except Exception:
            continue
        if 'clash' in dn.lower() or 'flclash' in dn.lower() or 'sing' in dn.lower():
            vals = {}
            for n in ('DisplayName', 'DisplayVersion', 'InstallDate', 'InstallLocation',
                      'DisplayIcon', 'UninstallString', 'Publisher'):
                try:
                    vals[n] = winreg.QueryValueEx(kk, n)[0]
                except Exception:
                    pass
            print('  键:', sub)
            for n, v in vals.items():
                print('    %-18s %s' % (n, v))
        winreg.CloseKey(kk)
    winreg.CloseKey(k)

print()
print('=== D. 联想电脑管家目录时间（佐证是否长期存在）===')
for d in (r'D:\Lenovo', r'D:\Lenovo\PCManager', r'D:\Lenovo\PCManager\5.1.140.9262'):
    if os.path.isdir(d):
        st = os.stat(d)
        print('  %-46s 创建=%s 修改=%s' % (d, ts(st.st_ctime), ts(st.st_mtime)))
    else:
        print('  %-46s (不存在)' % d)

print()
print('=== E. 11/4 之后 DriverStore 新增的驱动包目录（前 20）===')
root = r'C:\Windows\System32\DriverStore\FileRepository'
rows2 = []
try:
    for d in os.listdir(root):
        p = os.path.join(root, d)
        try:
            st = os.stat(p)
        except OSError:
            continue
        if st.st_ctime >= CUT:
            rows2.append((st.st_ctime, d))
except Exception as e:
    print('  读取失败:', e)
for t, d in sorted(rows2, reverse=True)[:20]:
    print('  %s  %s' % (ts(t), d))
if not rows2:
    print('  (无)')
