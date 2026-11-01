"""在窗口内做一段按住左键的横向拖动，用来验证列表行的拖动平移。"""
import argparse
import ctypes
import sys
import time
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8")
u = ctypes.windll.user32
LD, LU, MOVE = 0x02, 0x04, 0x0001


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--hwnd", type=int, required=True)
    ap.add_argument("--x0", type=int, required=True)
    ap.add_argument("--y", type=int, required=True)
    ap.add_argument("--x1", type=int, required=True)
    ap.add_argument("--steps", type=int, default=18)
    ap.add_argument("--hold", type=float, default=0.05)
    args = ap.parse_args()

    u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    r = wintypes.RECT()
    u.GetWindowRect(args.hwnd, ctypes.byref(r))

    def go(dx):
        u.SetCursorPos(r.left + dx, r.top + args.y)
        u.mouse_event(MOVE, 0, 0, 0, 0)
        time.sleep(args.hold)

    go(args.x0)
    u.mouse_event(LD, 0, 0, 0, 0)
    time.sleep(0.08)
    for i in range(1, args.steps + 1):
        go(args.x0 + (args.x1 - args.x0) * i // args.steps)
    time.sleep(0.12)
    u.mouse_event(LU, 0, 0, 0, 0)
    print(f"dragged {args.x0}->{args.x1} at y={args.y} win=({r.left},{r.top})")


if __name__ == "__main__":
    main()
