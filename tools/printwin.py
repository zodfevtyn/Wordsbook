"""PrintWindow 抓 Wordsbook 窗口：不抢焦点、不受遮挡影响，适合用户正在别的窗口里干活时验证 UI。

注意：由 DWM 单独合成的背景（Mica/Acrylic）不一定能被打进这张图，
如果侧栏在这张图里是黑的，需要用 grab.py 做真截屏复核。
"""

import argparse
import ctypes
import time
from ctypes import wintypes

from PIL import Image

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32

PW_RENDERFULLCONTENT = 0x0002
DIB_RGB_COLORS = 0
BI_RGB = 0


class RECT(ctypes.Structure):
    _fields_ = [("left", wintypes.LONG), ("top", wintypes.LONG),
                ("right", wintypes.LONG), ("bottom", wintypes.LONG)]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [("biSize", wintypes.DWORD), ("biWidth", ctypes.c_long),
                ("biHeight", ctypes.c_long), ("biPlanes", wintypes.WORD),
                ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
                ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", ctypes.c_long),
                ("biYPelsPerMeter", ctypes.c_long), ("biClrUsed", wintypes.DWORD),
                ("biClrImportant", wintypes.DWORD)]


def hwnd_for(title):
    found = []

    @ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
    def proc(h, _):
        if not user32.IsWindowVisible(h):
            return True
        n = user32.GetWindowTextLengthW(h)
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(h, buf, n + 1)
        if (buf.value or "") == title and not user32.IsIconic(h):
            found.append(h)
        return True

    user32.EnumWindows(proc, 0)
    return found[0] if found else None


def grab(h, out):
    r = RECT()
    if not user32.GetWindowRect(h, ctypes.byref(r)):
        raise SystemExit("GetWindowRect failed")
    w, hgt = r.right - r.left, r.bottom - r.top
    if w < 40 or hgt < 40:
        raise SystemExit(f"unexpected size {w}x{hgt}")

    sdc = user32.GetWindowDC(h)
    mdc = gdi32.CreateCompatibleDC(sdc)
    bmp = gdi32.CreateCompatibleBitmap(sdc, w, hgt)
    gdi32.SelectObject(mdc, bmp)
    ok = user32.PrintWindow(h, mdc, PW_RENDERFULLCONTENT)

    hdr = BITMAPINFOHEADER()
    hdr.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    hdr.biWidth, hdr.biHeight = w, -hgt
    hdr.biPlanes, hdr.biBitCount, hdr.biCompression = 1, 32, BI_RGB
    buf = ctypes.create_string_buffer(w * hgt * 4)
    got = gdi32.GetDIBits(mdc, bmp, 0, hgt, buf, ctypes.byref(hdr), DIB_RGB_COLORS)

    image = Image.frombuffer("RGBA", (w, hgt), buf, "raw", "BGRA", 0, 1)
    image.convert("RGB").save(out)

    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mdc)
    user32.ReleaseDC(h, sdc)
    print(f"printwindow={bool(ok)} dibits={got} size={w}x{hgt} out={out}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--title", default="Wordsbook")
    ap.add_argument("--delay", type=float, default=0.0)
    args = ap.parse_args()

    user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    if args.delay:
        time.sleep(args.delay)
    h = hwnd_for(args.title)
    if not h:
        raise SystemExit("window not found")
    grab(h, args.out)


if __name__ == "__main__":
    main()
