"""把 Wordsbook 窗口挪到指定位置/尺寸，用于让弹窗露出更多内容。"""
import ctypes, sys, time
from ctypes import wintypes

u = ctypes.windll.user32
x, y, w, h = (int(v) for v in sys.argv[1:5])

pid = int(sys.argv[5]) if len(sys.argv) > 5 else None
if pid is None:
    import subprocess
    pid = int(subprocess.check_output([
        "powershell", "-NoProfile", "-Command",
        "(Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1).Id"
    ]).decode().strip())

found = []
Proto = ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)


def cb(hwnd, _):
    p = wintypes.DWORD()
    u.GetWindowThreadProcessId(hwnd, ctypes.byref(p))
    if p.value == pid and u.IsWindowVisible(hwnd):
        found.append(hwnd)
    return True


u.EnumWindows(Proto(cb), 0)
hwnd = found[0]
u.MoveWindow(hwnd, x, y, w, h, True)
time.sleep(1.0)
r = wintypes.RECT()
u.GetWindowRect(hwnd, ctypes.byref(r))
print(f"hwnd={hwnd} rect={r.left},{r.top} {r.right - r.left}x{r.bottom - r.top}")
