"""Prepare embedded, offline WebGL paint atlases from the orthographic art sheets."""

import base64
import io
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from coat_relief import leather_mask, relief_map


ART = Path(__file__).resolve().parent / "cat-lab" / "art"
EYES_AND_EARS = {
    "adult": [(274,244,66,35), (423,236,60,38), (940,238,58,45),
              (235,120,58,77), (439,93,46,63), (977,109,35,69), (1065,114,67,84),
              (358,293,25,17), (855,285,18,14)],
    "kitten": [(310,352,56,42), (479,351,49,42), (1016,345,47,43),
               (395,410,31,19), (904,391,18,15)]
}
KITTEN_EAR_INTERIORS = [
    [(192,156),(228,172),(281,214),(237,253),(200,305),(181,251),(181,210)],
    [(572,158),(586,205),(585,254),(566,296),(526,252),(488,219),(533,178)],
    [(1036,145),(1053,166),(1075,199),(1024,215)],
    [(1168,145),(1191,180),(1205,232),(1194,308),(1168,271),(1122,228)]
]


def extend_edge(image, mask):
    """Spread boundary colors under UV seams using an alpha-weighted pyramid."""
    rgba = image.convert("RGBA")
    rgba.putalpha(mask)
    levels = [rgba.convert("RGBa")]
    while max(levels[-1].size)>1:
        level=levels[-1]
        levels.append(level.resize((max(level.width//2,1),max(level.height//2,1)),Image.Resampling.BOX))
    filled = levels.pop().convert("RGBA").convert("RGB")
    for level in reversed(levels):
        layer = level.convert("RGBA")
        filled = filled.resize(level.size, Image.Resampling.BILINEAR)
        filled.paste(layer, mask=layer.getchannel("A"))
    return filled


def encoded(image, fmt, **kwargs):
    buf = io.BytesIO()
    image.save(buf, format=fmt, **kwargs)
    return "data:image/"+fmt.lower()+";base64," + base64.b64encode(buf.getvalue()).decode("ascii")


def atlas(stage):
    image=Image.open(ART / f"calico-{stage}.webp").convert("RGB")
    r,g,b=np.asarray(image,dtype=float).transpose(2,0,1)
    chroma=(r>150) & (b>155) & (b>g*1.06) & (r>g*1.06)
    mask=Image.fromarray((~chroma).astype(np.uint8)*255).filter(ImageFilter.MedianFilter(3))
    fill=extend_edge(image,mask)
    r,g,b=np.asarray(fill,dtype=float).transpose(2,0,1)
    cream=(g>.67*r) & (b>.43*r) & (r>1.16*b) & (r>110)
    small=(512,round(512*image.height/image.width))
    coverage=Image.fromarray(cream.astype(np.uint8)*255).resize(small,Image.Resampling.BOX)
    inside=coverage.filter(ImageFilter.MedianFilter(5)).filter(ImageFilter.GaussianBlur(1.6)).point(
        lambda value: 255 if value>=128 else 0)
    outside=inside.copy()
    distance=np.zeros((small[1],small[0]),dtype=float)
    for _ in range(26):
        distance += (np.asarray(inside)>127).astype(float) - (np.asarray(outside)<128).astype(float)
        inside=inside.filter(ImageFilter.MinFilter(3))
        outside=outside.filter(ImageFilter.MaxFilter(3))
    signed=Image.fromarray(np.uint8(np.clip(127.5+distance*4.8,0,255)))
    protect=Image.new("L",image.size)
    draw=ImageDraw.Draw(protect)
    for x,y,rx,ry in EYES_AND_EARS[stage]:
        draw.ellipse((x-rx,y-ry,x+rx,y+ry),fill=255)
    if stage=="kitten":
        for polygon in KITTEN_EAR_INTERIORS:
            draw.polygon(polygon,fill=255)
    protect=protect.resize(small,Image.Resampling.LANCZOS).filter(ImageFilter.GaussianBlur(1.5))
    local_value=fill.convert("L").resize(small,Image.Resampling.BOX).filter(ImageFilter.GaussianBlur(1.8))
    fields=Image.merge("RGB",(signed,protect,local_value))
    # Use RGB for the masks so WebGL can sample both values independently,
    # including fully unprotected coat texels.
    grain, features = relief_map(fill,stage).split()
    finish = Image.merge("RGB",(grain,features,Image.new("L",fill.size)))
    return {"paint":encoded(fill,"WEBP",quality=91,method=5),
            "fields":encoded(fields,"PNG",optimize=True),
            "finish":encoded(finish,"WEBP",quality=96,method=5),
            "leather":encoded(leather_mask(fill.size,stage),"PNG",optimize=True)}


if __name__ == "__main__":
    data={stage:atlas(stage) for stage in EYES_AND_EARS}
    (ART / "atlases.js").write_text("window.CatAtlases="+json.dumps(data,separators=(",",":"))+";\n","utf-8")
