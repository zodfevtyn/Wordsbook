"""向 XAML 岛的子 HWND 直接投递鼠标消息来做横向拖动。

真鼠标在会话锁屏时进不去应用，而 WinUI 3 的指针输入最终来自桥接窗口的消息队列，
PostMessage 仍能送达。坐标用客户区物理像素（MAKELPARAM），y 加岛自己的偏移。
"""
import argparse
import ctypes
import sys
import time
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8")
u = ctypes.windll.user32
WM_MOUSEMOVE, WM_LBUTTONDOWN, WM_LBUTTONUP = 0x0200, 0x0201, 0x0202
MK_LBUTTON = 0x0001
Bridge = "Microsoft.UI.Content.DesktopChildSiteBridge"


def lp(x, y):
    return (y << 16) | (x & 0xFFFF)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--hwnd", type=int, required=True)
    ap.add_argument("--x0", type=int, required=True)
    ap.add_argument("--x1", type=int, required=True)
    ap.add_argument("--y", type=int, required=True)
    ap.add_argument("--steps", type=int, default=24)
    args = ap.parse_args()

    u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    bridge = u.FindWindowExW(args.hwnd, 0, Bridge, None)
    target = bridge or args.hwnd
    r = wintypes.RECT()
    u.GetWindowRect(args.hwnd, ctypes.byref(r))
    b = wintypes.RECT()
    u.GetWindowRect(target, ctypes.byref(b))
    ox, oy = b.left - r.left, b.top - r.top
    print(f"target={target} bridgeOff=({ox},{oy}) win=({r.left},{r.top})")

    def go(dx, dy, msg, wk):
        u.PostMessageW(target, msg, wk, lp(dx + ox, dy + oy))
        time.sleep(0.03)

    go(args.x0, args.y, WM_MOUSEMOVE, 0)
    go(args.x0, args.y, WM_LBUTTONDOWN, MK_LBUTTON)
    for i in range(1, args.steps + 1):
        x = args.x0 + (args.x1 - args.x0) * i // args.steps
        go(x, args.y, WM_MOUSEMOVE, MK_LBUTTON)
    time.sleep(0.08)
    go(args.x1, args.y, WM_LBUTTONUP, 0)
    print(f"posted {args.x0}->{args.x1} at y={args.y}")


if __name__ == "__main__":
    main()
