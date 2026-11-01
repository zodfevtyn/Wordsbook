"""圆角最终验收：四角是否真透出桌面、顶边有没有 1px 黑线、侧栏毛玻璃有没有在跟桌面混合。

先把窗口挪到同屏另一处抓一张"没有窗口的桌面"作参考，再放回原位逐角比对，
这样"角上是壁纸"和"角上是黑底"就不会靠肉眼猜。坐标一律物理像素。
"""

import ctypes
import sys
import time

from PIL import Image, ImageGrab

from grab import RECT, force_foreground, hwnd_for

user32 = ctypes.windll.user32
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))

X, Y, W, H = (int(a) for a in sys.argv[1:5])
h = hwnd_for("Wordsbook")
if not h:
    raise SystemExit("no window")


def place(x, y, wait=1.3):
    user32.SetWindowPos(h, 0, x, y, W, H, 0x0010)
    time.sleep(wait)


def shot():
    r = RECT()
    user32.GetWindowRect(h, ctypes.byref(r))
    return ImageGrab.grab(bbox=(r.left, r.top, r.right, r.bottom), all_screens=True), r


place(X, Y)
force_foreground(h)
time.sleep(1.0)
win, r = shot()
assert (r.left, r.top) == (X, Y) and (r.right - r.left, r.bottom - r.top) == (W, H), (r.left, r.top, r.right - r.left, r.bottom - r.top)
w, hh = win.size

# 桌面参考：把窗口整个藏起来抓一张。挪开不行——只要还压着原矩形，角上量到的就是窗口自己。
user32.ShowWindow(h, 0)  # SW_HIDE
time.sleep(0.5)
desk = ImageGrab.grab(bbox=(X, Y, X + W, Y + H), all_screens=True)
user32.ShowWindow(h, 5)  # SW_SHOW
place(X, Y)
force_foreground(h)
time.sleep(1.0)

corners = {"tl": (0, 0), "tr": (w - 1, 0), "bl": (0, hh - 1), "br": (w - 1, hh - 1)}
for k, (x, y) in corners.items():
    a, b = win.getpixel((x, y)), desk.getpixel((x, y))
    d = sum(abs(p - q) for p, q in zip(a, b))
    print(f"{k:2s} win={a} desk={b} diff={d} {'OK 透出桌面' if d < 40 else 'BAD 不是桌面'}")

black = sum(1 for x in range(w) if win.getpixel((x, 0)) == (0, 0, 0))
print(f"顶边第 0 行黑像素 {black}/{w}")

side = [win.getpixel((12, int(hh * f))) for f in (0.12, 0.35, 0.6, 0.85)]
spread = max(sum(side[i]) - min(sum(side[i]) for i in range(4)) for i in range(4))
print("侧栏取样", side, "极差", spread, "OK 毛玻璃在混合" if spread > 25 else "BAD 一块死灰")
win.resize((w // 3, hh // 3)).save("v-full.png")
win.crop((0, 0, 200, 200)).resize((600, 600), Image.NEAREST).save("v-tl-3x.png")
win.crop((w - 200, hh - 200, w, hh)).resize((600, 600), Image.NEAREST).save("v-br-3x.png")
