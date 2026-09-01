"""Rasterize Natural Earth land to a packed 1-bit lon/lat mask."""
from __future__ import annotations

import json
import struct
import urllib.request
from pathlib import Path

from PIL import Image, ImageDraw

W, H = 2880, 1440
URLS = [
    "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_land.geojson",
    "https://cdn.jsdelivr.net/gh/nvkelso/natural-earth-vector@master/geojson/ne_50m_land.geojson",
    "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_land.geojson",
]

ROOT = Path(r"D:\Projects\engine\games\Campaign\Content\Earth")
ROOT.mkdir(parents=True, exist_ok=True)


def project(lon: float, lat: float) -> tuple[float, float]:
    x = (lon + 180.0) / 360.0 * W
    y = (90.0 - lat) / 180.0 * H
    return x, y


def rings_of(geom: dict) -> list[list[list[list[float]]]]:
    t = geom["type"]
    if t == "Polygon":
        return [geom["coordinates"]]
    if t == "MultiPolygon":
        return geom["coordinates"]
    return []


def draw_ring(draw: ImageDraw.ImageDraw, ring: list[list[float]], fill: int) -> None:
    if len(ring) < 3:
        return
    pts = [project(p[0], p[1]) for p in ring]
    # Split on antimeridian jumps so PIL does not draw a belt across the map.
    runs: list[list[tuple[float, float]]] = [[]]
    for pt in pts:
        if runs[-1] and abs(pt[0] - runs[-1][-1][0]) > W * 0.5:
            runs.append([])
        runs[-1].append(pt)
    if len(runs) > 1 and runs[0] and runs[-1]:
        # Close wrap: draw each run separately.
        pass
    for run in runs:
        if len(run) >= 3:
            draw.polygon(run, fill=fill)


def load_geojson() -> dict:
    cache = ROOT / "ne_land.geojson"
    if cache.exists() and cache.stat().st_size > 10_000:
        return json.loads(cache.read_text(encoding="utf-8"))
    last_err: Exception | None = None
    for url in URLS:
        try:
            print("fetch", url)
            req = urllib.request.Request(url, headers={"User-Agent": "ember-earth-mask"})
            with urllib.request.urlopen(req, timeout=120) as r:
                data = r.read()
            cache.write_bytes(data)
            print("wrote", cache, "bytes", len(data))
            return json.loads(data.decode("utf-8"))
        except Exception as e:
            last_err = e
            print("fail", url, e)
    raise SystemExit(last_err)


def pack(img: Image.Image, path: Path) -> None:
    px = img.tobytes()
    bits = bytearray((W * H + 7) // 8)
    for i, v in enumerate(px):
        if v >= 128:
            bits[i >> 3] |= 1 << (i & 7)
    path.write_bytes(struct.pack("<HH", W, H) + bits)
    print("mask", path, "bytes", path.stat().st_size)


def preview_b64_360(img: Image.Image) -> str:
    small = img.resize((360, 180), Image.Resampling.BILINEAR)
    rows = []
    for y in range(180):
        acc = 0
        line = []
        for x in range(360):
            bit = 1 if small.getpixel((x, y)) >= 128 else 0
            acc = (acc << 1) | bit
            if x % 6 == 5:
                line.append("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_"[acc])
                acc = 0
        rows.append("".join(line))
    return "\n".join(rows)


def main() -> None:
    geo = load_geojson()
    img = Image.new("L", (W, H), 0)
    draw = ImageDraw.Draw(img)
    n = 0
    for feat in geo["features"]:
        for poly in rings_of(feat["geometry"]):
            if not poly:
                continue
            draw_ring(draw, poly[0], 255)
            for hole in poly[1:]:
                draw_ring(draw, hole, 0)
            n += 1
    print("polygons", n, "land px", sum(1 for p in img.tobytes() if p >= 128))
    pack(img, ROOT / "land.bin")
    img.resize((720, 360), Image.Resampling.NEAREST).save(ROOT / "land_preview.png")
    (ROOT / "land360.txt").write_text(preview_b64_360(img), encoding="ascii")
    print("preview", ROOT / "land_preview.png")


if __name__ == "__main__":
    main()
