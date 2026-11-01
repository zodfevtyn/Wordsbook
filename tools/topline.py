"""扫描不同窗口状态下窗口顶边第一行的颜色，定位那条 1px 白线出现的条件。"""
import argparse
import ctypes
import sys
import time
from ctypes import wintypes

from PIL import ImageGrab

sys.stdout.reconfigure(encoding="utf-8")
u = ctypes.windll.user32
k = ctypes.windll.kernel32


def hwnd_for(title="Wordsbook"):
    res = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
    def cb(h, _):
        if not u.IsWindowVisible(h):
            return True
        n = u.GetWindowTextLengthW(h)
        b = ctypes.create_unicode_buffer(n + 1)
        u.GetWindowTextW(h, b, n + 1)
        if title in (b.value or ""):
            res.append(h)
        return True

    u.EnumWindows(cb, 0)
    for h in res:
        if not u.IsIconic(h):
            return h
    return res[0] if res else None


def foreground(h):
    fg = u.GetForegroundWindow()
    cur = k.GetCurrentThreadId()
    ft = u.GetWindowThreadProcessId(fg, None)
    if ft and ft != cur:
        u.AttachThreadInput(cur, ft, True)
    u.BringWindowToTop(h)
    u.SetForegroundWindow(h)
    u.SetActiveWindow(h)
    if ft and ft != cur:
        u.AttachThreadInput(cur, ft, False)
    return fg


def report(tag, h, x, y, w, hh, delay, activate):
    u.SetWindowPos(h, 0, x, y, w, hh, 0x0010)
    time.sleep(0.5)
    if activate:
        foreground(h)
    else:
        other = hwnd_for("Qoder") or hwnd_for("Program Manager")
        if other:
            foreground(other)
    time.sleep(delay)
    r = wintypes.RECT()
    u.GetWindowRect(h, ctypes.byref(r))
    im = ImageGrab.grab(bbox=(r.left, r.top, r.right, r.bottom), all_screens=True).convert("RGB")
    W, H = im.size
    px = im.load()
    lines = []
    for row in range(3):
        vals = [px[cx, row] for cx in range(40, W - 40, 7)]
        light = sum(1 for c in vals if min(c) > 150)
        lines.append("y%d light=%d/%d med=%s" % (row, light, len(vals), sorted(vals)[len(vals) // 2]))
    print("%-26s rect=%d,%d %dx%d active=%s | %s" % (tag, r.left, r.top, W, H, u.GetForegroundWindow() == h, "  ".join(lines)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--monitor", default="primary")
    args = ap.parse_args()
    u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    h = hwnd_for()
    if not h:
        raise SystemExit("no window")

    cases = [
        ("float 20,19", 20, 19, 1937, 1281, 1.2, True),
        ("float top y=0", 300, 0, 1200, 900, 1.2, True),
        ("float slow settle", 20, 19, 1937, 1281, 3.0, True),
        ("float fast after move", 20, 19, 1937, 1281, 0.12, True),
        ("float inactive", 20, 19, 1937, 1281, 1.2, False),
        ("tiny 900x600", 400, 200, 900, 600, 1.2, True),
        ("second monitor", -2540, 20, 1200, 900, 1.2, True),
    ]
    for tag, x, y, w, hh, d, a in cases:
        report(tag, h, x, y, w, hh, d, a)


if __name__ == "__main__":
    main()
