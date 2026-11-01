"""真截屏验证毛玻璃：把窗口置顶、选一天、按需滚动并抓图。

PrintWindow 拍不到 DWM/合成器画的 backdrop，所以这里一律用 BitBlt 抓屏幕；
每帧 ~8ms，足够抓下 250ms 的动画中间帧。坐标全部物理像素。
"""

import argparse
import ctypes
import subprocess
import time
from ctypes import wintypes

from PIL import Image

import grab as G

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
HERE = r"D:\Proj\vs26\Wordsbook\tools"


class RECT(ctypes.Structure):
    _fields_ = [("l", ctypes.c_long), ("t", ctypes.c_long),
                ("r", ctypes.c_long), ("b", ctypes.c_long)]


class BIH(ctypes.Structure):
    _fields_ = [("s", wintypes.DWORD), ("w", ctypes.c_long), ("h", ctypes.c_long),
                ("p", wintypes.WORD), ("c", wintypes.WORD), ("comp", wintypes.DWORD),
                ("sz", wintypes.DWORD), ("x", ctypes.c_long), ("y", ctypes.c_long),
                ("cu", wintypes.DWORD), ("ci", wintypes.DWORD)]


class Shooter:
    def __init__(self, title="Wordsbook"):
        user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
        self.h = G.hwnd_for(title)
        if not self.h:
            raise SystemExit("window not found")
        self.prev = G.force_foreground(self.h)
        time.sleep(0.5)

    def rect(self):
        r = RECT()
        user32.GetWindowRect(self.h, ctypes.byref(r))
        return r

    def shot(self, name=None, w=None, h=None, x=0, y=0):
        r = self.rect()
        w = w or (r.r - r.l)
        h = h or (r.b - r.t)
        sdc = user32.GetDC(0)
        mdc = gdi32.CreateCompatibleDC(sdc)
        bmp = gdi32.CreateCompatibleBitmap(sdc, w, h)
        gdi32.SelectObject(mdc, bmp)
        hdr = BIH()
        hdr.s = ctypes.sizeof(BIH)
        hdr.w, hdr.h, hdr.p, hdr.c, hdr.comp = w, -h, 1, 32, 0
        buf = ctypes.create_string_buffer(w * h * 4)
        gdi32.BitBlt(mdc, 0, 0, w, h, sdc, r.l + x, r.t + y, 0x00CC0020)
        gdi32.GetDIBits(mdc, bmp, 0, h, buf, ctypes.byref(hdr), 0)
        img = Image.frombuffer("RGB", (w, h), buf, "raw", "BGRX", 0, 1)
        user32.ReleaseDC(0, sdc)
        gdi32.DeleteDC(mdc)
        gdi32.DeleteObject(bmp)
        if name:
            img.save(name)
        return img

    def click_at(self, x, y):
        user32.SetCursorPos(x, y)
        time.sleep(0.05)
        user32.mouse_event(0x0002, 0, 0, 0, 0)
        user32.mouse_event(0x0004, 0, 0, 0, 0)
        time.sleep(0.4)

    def click_window(self, wx, wy):
        r = self.rect()
        self.click_at(r.l + wx, r.t + wy)

    def wheel(self, notches, wx=900, wy=600):
        r = self.rect()
        user32.SetCursorPos(r.l + wx, r.t + wy)
        time.sleep(0.15)
        step = -120 if notches > 0 else 120
        for _ in range(abs(notches)):
            user32.mouse_event(0x0800, 0, 0, step, 0)
            time.sleep(0.03)
        time.sleep(0.6)

    def invoke(self, automation_id):
        out = subprocess.run(["powershell", "-NoProfile", "-File", f"{HERE}\\invoke-id.ps1",
                              "-Id", automation_id], capture_output=True, text=True)
        return out.stdout.strip()

    def invoke_name(self, name):
        out = subprocess.run(["powershell", "-NoProfile", "-File", f"{HERE}\\invoke-by-name.ps1",
                              "-Name", name], capture_output=True, text=True)
        return out.stdout.strip()

    def text_center(self, name):
        """按 UIA Name 找文本中心点（侧栏行不是 Button，只能按坐标点）。"""
        out = subprocess.run(["powershell", "-NoProfile", "-File", f"{HERE}\\uia-find.ps1",
                              "-NamePattern", name], capture_output=True, text=True).stdout
        for line in out.splitlines():
            if f"name='{name}'" not in line:
                continue
            geo = line.split("rect=")[1].split()
            x, y = (int(v) for v in geo[0].split(","))
            w, h = (int(v) for v in geo[1].split("x"))
            return x + w // 2, y + h // 2
        return None

    def done(self):
        if self.prev:
            user32.SetForegroundWindow(self.prev)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--day", help="侧栏里要选中的日期文本，例如 'Wed, 7'")
    ap.add_argument("--wheel", type=int, default=0, help="向下滚动的格数（负数向上）")
    ap.add_argument("--box", type=int, nargs=4, help="窗口内裁剪 x y w h")
    args = ap.parse_args()

    s = Shooter()
    if args.day:
        c = s.text_center(args.day)
        if not c:
            raise SystemExit(f"day row '{args.day}' not found")
        s.click_at(*c)
        time.sleep(1.2)
    if args.wheel:
        s.wheel(args.wheel)
    img = s.shot()
    if args.box:
        img = img.crop(tuple(args.box))
    img.save(args.out)
    s.done()
    print("saved", args.out, img.size)


if __name__ == "__main__":
    main()
