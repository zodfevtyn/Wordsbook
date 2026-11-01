"""把 Wordsbook 窗口置顶后按物理像素真截屏。

PrintWindow 只抓应用自己绘制的内容，Mica/Acrylic 这类由 DWM 合成的背景会拍成黑块，
所以验证毛玻璃必须走真截屏。坐标一律物理像素，进程先声明 DPI 感知，避免 GetWindowRect
被系统虚拟化后和截屏尺寸对不上。
"""

import argparse
import ctypes
import time
from ctypes import wintypes

from PIL import ImageGrab

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32


class RECT(ctypes.Structure):
    _fields_ = [("left", wintypes.LONG), ("top", wintypes.LONG),
                ("right", wintypes.LONG), ("bottom", wintypes.LONG)]


def hwnd_for(title="Wordsbook"):
    best = None
    result = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
    def enum_proc(h, _):
        if not user32.IsWindowVisible(h):
            return True
        length = user32.GetWindowTextLengthW(h)
        buf = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(h, buf, length + 1)
        if title in (buf.value or ""):
            result.append(h)
        return True

    user32.EnumWindows(enum_proc, 0)
    for h in result:
        if not user32.IsIconic(h):
            best = h
            break
    return best or (result[0] if result else None)


def force_foreground(h):
    """跨进程抢前台：SetForegroundWindow 有防抢焦点限制，先把自己的线程挂到当前前台线程上。"""
    fg = user32.GetForegroundWindow()
    cur = kernel32.GetCurrentThreadId()
    fg_thread = user32.GetWindowThreadProcessId(fg, None)
    if fg_thread and fg_thread != cur:
        user32.AttachThreadInput(cur, fg_thread, True)
    user32.ShowWindow(h, 9)  # SW_RESTORE
    user32.BringWindowToTop(h)
    user32.SetForegroundWindow(h)
    user32.SetActiveWindow(h)
    if fg_thread and fg_thread != cur:
        user32.AttachThreadInput(cur, fg_thread, False)
    return fg


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--x", type=int)
    ap.add_argument("--y", type=int)
    ap.add_argument("--w", type=int)
    ap.add_argument("--h", type=int)
    ap.add_argument("--delay", type=float, default=0.9)
    ap.add_argument("--title", default="Wordsbook")
    ap.add_argument("--restore-focus", action="store_true",
                    help="截完把前台还给原来的窗口")
    args = ap.parse_args()

    user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    h = hwnd_for(args.title)
    if not h:
        raise SystemExit("no Wordsbook window found")

    if args.x is not None and args.w:
        user32.SetWindowPos(h, 0, args.x, args.y, args.w, args.h, 0x0010)  # NOACTIVATE
    prev_fg = force_foreground(h)
    time.sleep(args.delay)

    r = RECT()
    user32.GetWindowRect(h, ctypes.byref(r))
    box = (max(-25600, r.left), max(-25600, r.top), r.right, r.bottom)
    im = ImageGrab.grab(bbox=box, all_screens=True)
    im.save(args.out)
    if args.restore_focus and prev_fg:
        user32.SetForegroundWindow(prev_fg)
    print(f"rect={box} size={im.size} out={args.out}")


if __name__ == "__main__":
    main()
