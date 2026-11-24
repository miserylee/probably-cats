"""Draw the approved rest poses on the unwarped slim-cat outlines (Pillow)."""

import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
OUTLINES = Path(__file__).with_name("cat_pose_contours.json")

# Joint locations use the native coordinates of the slim pose study. Screen
# coordinates increase rightwards/downwards. Near-side limbs are sufficient
# for evaluating these fixed, side-facing inventory poses.
ADULT = {
    "muzzle": (5, 49), "skull": (41, 44), "nape": (52, 57),
    "neck": (50, 76), "withers": (67, 97), "back": (88, 114),
    "loin": (105, 140), "sacrum": (114, 161),
    "shoulder": (45, 111), "elbow": (60, 144),
    "wrist": (50, 200), "forepaw": (43, 215),
    "hip": (99, 164), "stifle": (74, 161), "hock": (105, 208),
    "hindpaw": (71, 215),
    "tail_root": (119, 176), "tail_1": (128, 200),
    "tail_2": (125, 216), "tail_3": (105, 225),
    "tail_4": (69, 228), "tail_5": (41, 227), "tail_6": (23, 223),
    "tail_7": (20, 215), "tail_tip": (29, 207),
}
KITTEN = {
    "muzzle": (195, 44), "skull": (166, 39), "nape": (148, 40),
    "neck": (139, 38), "withers": (127, 35), "back": (109, 37),
    "loin": (92, 30), "sacrum": (71, 36),
    "shoulder": (137, 43), "elbow": (125, 54),
    "wrist": (151, 56), "forepaw": (168, 58),
    "hip": (76, 38), "stifle": (89, 47), "hock": (63, 56),
    "hindpaw": (87, 59),
    "tail_root": (59, 44), "tail_1": (54, 49), "tail_2": (46, 54),
    "tail_3": (36, 56), "tail_4": (27, 55), "tail_5": (18, 54),
    "tail_6": (13, 54), "tail_7": (8, 55), "tail_tip": (4, 57),
}

CHAINS = {
    "spine": "muzzle skull nape neck withers back loin sacrum",
    "foreleg": "withers shoulder elbow wrist forepaw",
    "hindleg": "sacrum hip stifle hock hindpaw",
    "tail": "sacrum tail_root tail_1 tail_2 tail_3 tail_4 tail_5 tail_6 tail_7 tail_tip",
}
COLORS = {"spine": "#EDE7D8", "foreleg": "#88CEC9",
          "hindleg": "#F3B476", "tail": "#9BA7BA"}


def make_preview(output):
    outlines = json.loads(OUTLINES.read_text("utf-8"))
    image = Image.new("RGB", (960, 748), "#192226")
    draw = ImageDraw.Draw(image)
    font_path = next((str(p) for p in (
        Path("C:/Windows/Fonts/msyh.ttc"),
        Path("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"),
    ) if p.is_file()), "DejaVuSans.ttf")

    def label(x, y, text, size=17, color="#DFE4DD", anchor="lm"):
        draw.text((x, y), text, font=ImageFont.truetype(font_path,size),
                  fill=color, anchor=anchor)

    def panel(stage, joints, cols, rows, middle, top, show_rig):
        cell = 94
        w, h = cell*cols, cell*rows
        left = middle-w//2
        draw.rectangle((left,top,left+w,top+h),fill="#222D31",outline="#465457")
        for i in range(1,cols):
            draw.line((left+i*cell,top,left+i*cell,top+h),fill="#39464A")
        for i in range(1,rows):
            draw.line((left,top+i*cell,left+w,top+i*cell),fill="#39464A")

        pose = outlines[stage]
        sw, sh = pose["size"]
        scale = min((w-12)/sw,(h-10)/sh)
        ox, oy = middle-sw*scale/2, top+(h-sh*scale)/2

        def project(point):
            return (ox+point[0]*scale, oy+point[1]*scale)

        mask = Image.new("L",image.size)
        brush = ImageDraw.Draw(mask)
        for filled,coords in pose["planes"]["outline"]:
            points = [project(pt) for pt in zip(coords[::2],coords[1::2])]
            brush.polygon(points,fill=255*filled)
        image.paste("#455257" if show_rig else "#BFCBC7",mask=mask)
        if not show_rig:
            return
        for chain,names in CHAINS.items():
            points = [project(joints[name]) for name in names.split()]
            draw.line(points,fill=COLORS[chain],width=2 if chain=="tail" else 3,
                      joint="curve")
        for chain,names in CHAINS.items():
            for name in names.split()[1:]:
                if name.startswith("tail") and name != "tail_tip":
                    continue
                x,y = project(joints[name])
                draw.ellipse((x-3,y-3,x+3,y+3),fill=COLORS[chain],outline="#263135")
        callouts = (("shoulder","肩", -110,-18),("hock","跗关节",69,9),
                    ("stifle","膝",92,-16)) if stage=="adult" else (
                    ("hip","髋",-48,-76),("elbow","肘",42,-78))
        for key,text,dx,dy in callouts:
            x,y = project(joints[key])
            end_x,end_y = x+dx,y+dy
            draw.line((x,y,end_x,end_y),fill="#8A9895",width=1)
            label(end_x+(5 if dx>0 else -5),end_y,text,15,
                  anchor="lm" if dx>0 else "rm")

    label(40,36,"深空猫舍 · 瘦猫姿势定稿",26)
    draw.line((40,72,920,72),fill="#425054")
    label(290,103,"瘦猫轮廓",19,anchor="mm")
    label(687,103,"同一轮廓上的关节",19,anchor="mm")
    for stage,joints,cols,rows,y,caption in (
        ("adult",ADULT,2,4,132,"成猫坐姿"),("kitten",KITTEN,3,1,574,"幼猫趴姿"),
    ):
        label(40,y+21,caption)
        label(40,y+48,f"{cols} × {rows}",16,"#A2B3B1")
        panel(stage,joints,cols,rows,290,y,False)
        panel(stage,joints,cols,rows,687,y,True)
    draw.line((40,697,920,697),fill="#425054")
    for i,(chain,title) in enumerate(zip(CHAINS,("头颈与脊柱","前肢","后肢","尾巴"))):
        x = 137+i*185
        draw.line((x,720,x+23,720),fill=COLORS[chain],width=3)
        label(x+32,720,title,15)
    image.save(output)


if __name__ == "__main__":
    make_preview(ROOT/"docs"/"cat-skeleton-review.png")
