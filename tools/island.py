"""报告 XAML 岛（DesktopChildSiteBridge）相对父窗口客户区的偏移，以及各子窗口尺寸。

白线的判据：岛顶比客户区顶低 1px，那一行就没人画。
"""
import ctypes
import sys
import time
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8")
u = ctypes.windll.user32

BRIDGE = "Microsoft.UI.Content.DesktopChildSiteBridge"


class RECT(ctypes.Structure):
    _fields_ = [("l", wintypes.LONG), ("t", wintypes.LONG),
                ("r", wintypes.LONG), ("b", wintypes.LONG)]


class POINT(ctypes.Structure):
    _fields_ = [("x", wintypes.LONG), ("y", wintypes.LONG)]


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


def probe(tag, h):
    u.GetClientRect(h, ctypes.byref(cr := RECT()))
    o = POINT()
    u.ClientToScreen(h, ctypes.byref(o))
    u.GetWindowRect(h, ctypes.byref(wr := RECT()))
    kids = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
    def cb(k, _):
        b = ctypes.create_unicode_buffer(256)
        u.GetClassNameW(k, b, 256)
        u.GetWindowRect(k, ctypes.byref(kr := RECT()))
        kids.append((b.value, kr.l - o.x, kr.t - o.y, kr.r - kr.l, kr.b - kr.t))
        return True

    u.EnumChildWindows(h, cb, 0)
    bridge = [x for x in kids if x[0] == BRIDGE]
    print("%-22s win=%dx%d client=%dx%d origin=(%d,%d)" % (tag, wr.r - wr.l, wr.b - wr.t, cr.r, cr.b, o.x, o.y))
    for name, dx, dy, w, hh in kids:
        mark = "  <== BRIDGE" if name == BRIDGE else ""
        gap = ""
        if bridge and name == BRIDGE:
            gap = " gapTop=%d gapBottom=%d gapRight=%d" % (dy, cr.b - (dy + hh), cr.r - (dx + w))
        print("    %-46s (%d,%d) %dx%d%s%s" % (name, dx, dy, w, hh, gap, mark))


def main():
    u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    h = hwnd_for()
    if not h:
        raise SystemExit("no window")
    probe("as-is", h)

    u.SetWindowPos(h, 0, 20, 19, 1937, 1281, 0x0010)
    time.sleep(1.0)
    probe("after move 1937x1281", h)

    u.ShowWindow(h, 6)  # SW_MINIMIZE
    time.sleep(0.8)
    u.ShowWindow(h, 9)  # SW_RESTORE
    time.sleep(1.2)
    probe("after min/restore", h)

    u.SetWindowPos(h, 0, 20, 19, 1200, 800, 0x0010)
    time.sleep(1.0)
    probe("after shrink 1200x800", h)

    u.SetWindowPos(h, 0, 0, 0, 2560, 1392, 0x0010)
    time.sleep(1.0)
    probe("cover work area", h)

    u.SetWindowPos(h, 0, 20, 19, 1937, 1281, 0x0010)
    time.sleep(1.0)
    probe("back to 1937x1281", h)


if __name__ == "__main__":
    main()
