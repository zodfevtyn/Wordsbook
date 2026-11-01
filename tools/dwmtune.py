"""跨进程对 Wordsbook 窗口实时改 DWM 属性，抓顶边第一行，定位那条 1px 白线的来源。

DwmSetWindowAttribute 作用于目标 HWND，不需要重启应用，所以可以在活窗口上做 A/B。
"""
import argparse
import ctypes
import sys
import time
from ctypes import wintypes

from PIL import ImageGrab

sys.stdout.reconfigure(encoding="utf-8")
u = ctypes.windll.user32
k = ctypes.windll.kernel32
d = ctypes.windll.dwmapi

CORNER = {1: "DONOTROUND", 2: "ROUND", 3: "SMALLROUND", 4: "NONE"}


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
    if ft and ft != cur:
        u.AttachThreadInput(cur, ft, False)


def set_attr(h, attr, value):
    v = ctypes.c_int(value)
    return d.DwmSetWindowAttribute(h, attr, ctypes.byref(v), 4)


def get_attr(h, attr):
    v = ctypes.c_int(0)
    hr = d.DwmGetWindowAttribute(h, attr, ctypes.byref(v), 4)
    return hr, v.value


def top_rows(h, n=3):
    r = wintypes.RECT()
    u.GetWindowRect(h, ctypes.byref(r))
    im = ImageGrab.grab(bbox=(r.left, r.top, r.right, r.bottom), all_screens=True).convert("RGB")
    W, H = im.size
    px = im.load()
    out = []
    for row in range(n):
        vals = [px[cx, row] for cx in range(60, W - 60, 7)]
        light = sum(1 for c in vals if min(c) > 150)
        out.append("y%d light=%d/%d med=%s" % (row, light, len(vals), sorted(vals)[len(vals) // 2]))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--x", type=int, default=20)
    ap.add_argument("--y", type=int, default=19)
    ap.add_argument("--w", type=int, default=1937)
    ap.add_argument("--h", type=int, default=1281)
    args = ap.parse_args()

    u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    h = hwnd_for()
    if not h:
        raise SystemExit("no window")
    u.SetWindowPos(h, 0, args.x, args.y, args.w, args.h, 0x0010)
    time.sleep(0.6)
    foreground(h)
    time.sleep(1.0)

    hr, cur = get_attr(h, 33)
    print("corner now: hr=%d val=%d %s" % (hr, cur, CORNER.get(cur, "?")))
    print("baseline   :", "  ".join(top_rows(h)))

    for attr, val, label in [
        (33, 2, "corner=ROUND"),
        (33, 1, "corner=DONOTROUND"),
        (2, 1, "NCRENDERING=DISABLED"),
        (2, 0, "NCRENDERING=ENABLED"),
        (34, -2, "BORDER_COLOR=COLOR_NONE"),
        (34, 0x00201C1C, "BORDER_COLOR=dark"),
        (34, 0x00FFFFFF, "BORDER_COLOR=white"),
    ]:
        hr = set_attr(h, attr, val)
        time.sleep(0.45)
        print("%-24s hr=%2d  %s" % (label, hr, "  ".join(top_rows(h))))


if __name__ == "__main__":
    main()
