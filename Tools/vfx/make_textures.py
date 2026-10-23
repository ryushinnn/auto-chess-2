"""Procedural VFX textures (white, shape in alpha; color comes from the material / particles).

  py Tools/vfx/make_textures.py      -> Assets/_main/Textures/VFX/vfx_*.png

Needs Pillow (py -m pip install --user pillow).
"""
import math
import random
from pathlib import Path

from PIL import Image

OUT = Path(__file__).resolve().parents[2] / "Assets" / "_main" / "Textures" / "VFX"


def save(name, size, alpha_fn):
    w, h = size
    img = Image.new("RGBA", size)
    px = img.load()
    for y in range(h):
        for x in range(w):
            u = (x + 0.5) / w * 2 - 1
            v = (y + 0.5) / h * 2 - 1
            a = max(0.0, min(1.0, alpha_fn(u, v, x, y)))
            px[x, y] = (255, 255, 255, int(a * 255 + 0.5))
    img.save(OUT / f"{name}.png")
    print("written", name)


def smooth(t):
    return t * t * (3 - 2 * t)


class Noise:
    """Tileable value noise on a grid of `cells`."""

    def __init__(self, cells, seed):
        rnd = random.Random(seed)
        self.cells = cells
        self.v = [[rnd.random() for _ in range(cells)] for _ in range(cells)]

    def __call__(self, x, y):
        c = self.cells
        x0, y0 = int(math.floor(x)) % c, int(math.floor(y)) % c
        fx, fy = smooth(x - math.floor(x)), smooth(y - math.floor(y))
        x1, y1 = (x0 + 1) % c, (y0 + 1) % c
        a = self.v[y0][x0] * (1 - fx) + self.v[y0][x1] * fx
        b = self.v[y1][x0] * (1 - fx) + self.v[y1][x1] * fx
        return a * (1 - fy) + b * fy

    def fbm(self, x, y, octaves=4):
        total, amp, norm = 0.0, 0.5, 0.0
        for i in range(octaves):
            total += self(x * 2 ** i, y * 2 ** i) * amp
            norm += amp
            amp *= 0.5
        return total / norm


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    n1 = Noise(8, 11)
    n2 = Noise(16, 23)

    # soft round glow
    save("vfx_glow", (128, 128), lambda u, v, *_: (1 - min(1, math.hypot(u, v))) ** 2.2)

    # thin ring with a soft inner fill
    def ring(u, v, *_):
        r = math.hypot(u, v)
        band = math.exp(-((r - 0.8) / 0.07) ** 2)
        inner = 0.15 * max(0.0, 1 - r / 0.8) ** 2
        return (band + inner) * (1 if r < 1 else 0)
    save("vfx_ring", (256, 256), ring)

    # four-point star
    def star(u, v, *_):
        r = math.hypot(u, v)
        rays = math.exp(-abs(u) * 18) * (1 - min(1, abs(v))) + math.exp(-abs(v) * 18) * (1 - min(1, abs(u)))
        core = (1 - min(1, r / 0.35)) ** 2
        return rays * 0.9 + core
    save("vfx_spark", (128, 128), star)

    # crescent slash (upper half arc, thick in the middle, tapered ends)
    def slash(u, v, *_):
        r = math.hypot(u, v * 1.25 + 0.25)
        angle = math.atan2(-(v * 1.25 + 0.25), u)
        if angle < 0:
            return 0
        along = math.sin(angle)
        width = 0.03 + 0.12 * along
        return math.exp(-((r - 0.72) / width) ** 2) * along ** 0.6
    save("vfx_slash", (256, 256), slash)

    # wind swirl: three spiral arms, faded at the center and the rim
    def swirl(u, v, *_):
        r = math.hypot(u, v)
        if r >= 1:
            return 0
        a = math.atan2(v, u)
        arms = 0.5 + 0.5 * math.cos(3 * a + 9 * r)
        arms = arms ** 6
        fade = smooth(min(1, r / 0.3)) * (1 - smooth(min(1, max(0, (r - 0.7) / 0.3))))
        return arms * fade * (0.6 + 0.4 * n1.fbm((u + 1) * 2, (v + 1) * 2))
    save("vfx_swirl", (256, 256), swirl)

    # smoke puff
    def smoke(u, v, *_):
        r = math.hypot(u, v)
        n = n2.fbm((u + 1) * 4, (v + 1) * 4)
        return max(0.0, (1 - r) * 1.6 - 0.25) * (0.4 + 0.9 * n) * (1 if r < 1 else 0)
    save("vfx_smoke", (128, 128), smoke)

    # flame tongue (points up: v = -1 is the top of the image)
    def flame(u, v, *_):
        y = (v + 1) / 2  # 0 top .. 1 bottom
        half = 0.55 * y ** 0.7 * (1 - y) ** 0.15
        if half <= 0:
            return 0
        side = abs(u + 0.15 * math.sin(y * 9)) / max(half, 1e-3)
        body = max(0.0, 1 - side ** 2)
        n = n1.fbm((u + 1) * 3, y * 6)
        return body * (0.5 + 0.7 * n) * min(1, (1 - y) * 6)
    save("vfx_flame", (128, 128), flame)

    # beam: bright core across U, streaks repeating along V (tileable)
    def beam(u, v, x, y):
        core = math.exp(-(u / 0.18) ** 2)
        edge = math.exp(-(u / 0.55) ** 2) * 0.45
        streak = 0.65 + 0.35 * n2.fbm(x / 128 * 2 + 3, y / 256 * 16)
        return (core + edge) * streak
    save("vfx_beam", (128, 256), beam)


if __name__ == "__main__":
    main()
