"""等会话解锁后自动跑一遍列表行横向拖动验证。

锁屏时 SendInput / SetCursorPos 都进不了应用，PostMessage 又驱动不了 WinUI 的指针管线，
所以只能等。解锁判据：锁屏那个顶层窗口（Windows.UI.Core.CoreWindow / 'Windows Default
Lock Screen'）消失。用输入桌面名判断不可靠——本进程拿到的永远是 "Default"。
"""
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8")


def locked():
    import ctypes
    return ctypes.windll.user32.FindWindowW(
        "Windows.UI.Core.CoreWindow", "Windows Default Lock Screen") != 0


def shell(cmd):
    print("$", cmd, flush=True)
    r = subprocess.run(cmd, shell=True, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.stdout:
        print(r.stdout.strip(), flush=True)
    if r.returncode:
        print("rc", r.returncode, (r.stderr or "").strip()[:400], flush=True)


deadline = time.time() + float(sys.argv[1] if len(sys.argv) > 1 else 1800)
while time.time() < deadline:
    if not locked():
        break
    time.sleep(3)
else:
    print("TIMEOUT still locked", flush=True)
    raise SystemExit(2)

print("unlocked at", time.strftime("%H:%M:%S"), flush=True)
time.sleep(3)
T = r"C:\Users\Lenovo\AppData\Local\Temp"
TOOLS = r"D:\Proj\vs26\Wordsbook\tools"
import ctypes

u = ctypes.windll.user32
u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
hwnd = u.FindWindowW(None, "Wordsbook")
print("hwnd", hex(hwnd), flush=True)
if not hwnd:
    print("ABORT: Wordsbook 窗口没了，重启动作要人工做", flush=True)
    raise SystemExit(3)


def esc():
    """关掉可能弹出的编辑框，别把界面留在弹窗状态。"""
    for vk in (0x1B, 0x1B):
        u.keybd_event(vk, 0, 0, 0)
        time.sleep(0.05)
        u.keybd_event(vk, 0, 2, 0)
        time.sleep(0.3)


shell(f'python "{TOOLS}\\place.py" 234 234 950 900')
time.sleep(1.5)

# 检查 1 + 3：超宽行左拖 150px 应平移带子，且不该顺手打开编辑弹窗。
# grab 自带置顶，所以 esc() 都排在 grab 之后——否则 Escape 会发给别的前台窗口。
shell(f'python "{TOOLS}\\grab.py" --out "{T}\\drag-before.png"')
esc()
shell(f'python "{TOOLS}\\grab.py" --out "{T}\\drag-before2.png"')
shell(f'python "{TOOLS}\\hdrag.py" --hwnd {hwnd} --x0 816 --y 397 --x1 666 --steps 30')
time.sleep(1.5)
shell(f'python "{TOOLS}\\grab.py" --out "{T}\\drag-after.png"')

# 检查 2：不超宽的行原地按下松开仍要能打开编辑器（拖动逻辑没把点击吃掉）。
esc()
shell(f'python "{TOOLS}\\hdrag.py" --hwnd {hwnd} --x0 816 --y 213 --x1 817 --steps 2')
time.sleep(1.5)
shell(f'python "{TOOLS}\\grab.py" --out "{T}\\click-narrow.png"')
esc()
time.sleep(0.8)
shell(f'python "{TOOLS}\\grab.py" --out "{T}\\click-closed.png"')

print("=== pan.log ===", flush=True)
try:
    print(open(T + r"\pan.log", encoding="utf-8").read()[-4000:], flush=True)
except OSError as e:
    print("no pan.log:", e, flush=True)
print("DONE", flush=True)
