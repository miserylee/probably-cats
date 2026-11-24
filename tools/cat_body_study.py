"""Render the gene-driven body study in the two approved inventory poses."""

import numpy as np
from PIL import Image, ImageDraw

from cat_coat_study import render_skin, soft_face
from cat_preview import Coat, Genome, PARENT_A, font
from cat_skeleton import ROOT


# Matched points on two complete drawings, expressed in the slim rig's units.
# Each row follows a continuous feature. Points *on the skin* are free to
# slide over the skeleton. The planted toes, eyes and ear tips register the poses.
# Coordinates are (slim x, slim y, full x, full y).
ADULT_SURFACE = (
    # Ears, brow and nose
    (25,3,25,3), (66,3,66,3), (43,23,43,23), (27,39,27,39),
    (4,49,6,50), (69,36,83,33), (57,28,67,26),
    (20,39,19,39), (35,39,36,39), (27,33,27,33), (27,46,27,46),
    (31,25,28,21), (18,32,14,30), (47,48,56,48),
    # Muzzle, underjaw, throat and the soft apron in front of the foreleg
    (9,59,6,64), (21,63,16,72), (28,69,16,77), (28,81,9,87),
    (22,98,7,105), (20,110,7,121), (21,121,10,137),
    (27,136,15,153), (32,151,21,166), (35,166,27,180),
    (39,187,35,194), (40,199,39,199),
    # Nape, shoulder and flank form one long sweep to the seated rump
    (71,47,94,47), (66,60,93,64), (64,72,89,74), (66,79,96,79),
    (71,85,101,84), (77,96,113,92), (84,100,121,99),
    (96,105,131,114), (106,113,141,126), (115,128,149,142),
    (122,148,155,160), (125,168,157,176), (124,189,156,190),
    # Throat plane, shoulder blade, underside and leading edge of the thigh
    (43,64,56,74), (49,80,48,92), (48,105,41,116),
    (37,131,32,151), (48,152,44,168), (49,180,47,185),
    (65,114,69,125), (70,134,77,140), (67,153,73,158),
    (62,169,65,174), (59,192,58,194),
    (89,132,104,134), (98,156,120,151), (106,181,132,180),
    (81,199,92,199),
    # Contact line and wrapped tail. The visible outer turn wraps the wider rump.
    (42,203,42,203), (43,215,43,215), (71,215,71,215),
    (28,207,28,207), (18,215,18,215), (25,224,25,224),
    (43,227,43,227), (71,229,72,229), (98,227,110,227),
    (120,221,143,220), (132,213,158,211), (133,201,160,201),
)

KITTEN_SURFACE = (
    # Ear tips, eye, muzzle and wrists
    (143,9,143,9), (173,2,173,2), (180,36,180,36), (197,44,197,44),
    (193,51,194,54), (185,57,185,58), (167,58,167,58),
    (174,22,174,18), (189,33,191,31), (160,42,156,43),
    (165,51,161,54),
    # One low loaf, from the tail root across the rump and shoulders to the cheek
    (43,50,39,48), (54,46,49,40), (61,35,55,29),
    (70,28,67,21), (81,24,81,17), (94,23,96,16),
    (104,25,107,16), (114,30,117,18), (124,31,127,20),
    (136,29,137,21), (143,30,143,25),
    # Haunch, flank, shoulder crease and the low belly between the planted paws
    (61,49,56,48), (76,41,72,37), (88,45,92,41),
    (105,48,109,48), (122,49,126,47), (139,45,143,46),
    (152,48,155,49), (59,62,55,63), (88,64,88,64),
    (102,62,102,63), (119,62,119,64), (135,63,136,64), (151,63,152,64),
    (168,64,168,64), (191,62,191,62),
    (4,56,4,56), (18,54,18,54), (34,54,33,54),
)


def bending_basis(points, centers):
    squared = ((points[:, None, :] - centers[None, :, :]) ** 2).sum(axis=2)
    return squared * np.log(np.maximum(squared, 1e-15))


def surface_coordinates(rest, surface, pixels):
    """Thin-plate spline from the interpolated skin back to its coat coordinates."""
    # Use a common scale on both axes to preserve the bending metric.
    origin = rest.mean(axis=0)
    unit = max(np.ptp(rest, axis=0))
    controls = (surface - origin) / unit
    linear = np.column_stack((np.ones(len(controls)), controls))
    system = np.block([[bending_basis(controls, controls), linear],
                       [linear.T, np.zeros((3, 3))]])
    rhs = np.vstack((rest - surface, np.zeros((3, 2))))
    weights = np.linalg.solve(system, rhs)
    coords = np.empty_like(pixels, dtype=float)
    for start in range(0, len(pixels), 2048):
        part = pixels[start:start+2048]
        uv = (part - origin) / unit
        bend = bending_basis(uv, controls) @ weights[:-3]
        coords[start:start+len(part)] = part + bend + weights[-3] + uv @ weights[-2:]
    return coords


def sample_coat(skin, uv):
    # Interpolate in premultiplied alpha so hairs along the silhouette keep
    # their color. Transparent padding also gives samples outside the cat zero alpha.
    texels = np.pad(np.asarray(skin.convert("RGBa")), ((1,1), (1,1), (0,0)))
    coords = np.clip(uv + 1, (0, 0), (skin.width + .999, skin.height + .999))
    x, y = np.floor(coords).astype(int).T
    fx, fy = (coords - np.floor(coords)).T[:, :, None]
    top = (1-fx)*texels[y, x] + fx*texels[y, x+1]
    bottom = (1-fx)*texels[y+1, x] + fx*texels[y+1, x+1]
    return (1-fy)*top + fy*bottom


def body_surface(genome, adult):
    traits = genome.express()
    condition = .74*traits["fullness"] + .26*traits["frame"]
    skin = render_skin(genome, adult)
    margin = (5, 3) if adult else (2, 3)
    size = (170, 242) if adult else (205, 70)
    if condition == 0 and traits["jowls"] == 0:
        canvas = Image.new("RGBA", size)
        canvas.paste(skin, margin)
        return canvas

    skin = soft_face(skin, Coat(traits), adult, .80*condition + .20*traits["jowls"])
    points = np.array(ADULT_SURFACE if adult else KITTEN_SURFACE, dtype=float)
    rest, full = points[:, :2], points[:, 2:]
    # Cheek expression blends along the jaw into the same surface as the trunk.
    center = (43, 62) if adult else (167, 45)
    radius = (28, 20) if adult else (21, 11)
    jaw = np.exp(-np.sum(((rest-center)/radius)**2, axis=1))
    amount = condition + .45*jaw*(traits["jowls"]-condition)
    surface = rest + amount[:, None]*(full-rest)
    yy, xx = np.indices((size[1], size[0]))
    pixels = np.column_stack((xx.ravel()-margin[0], yy.ravel()-margin[1]))
    uv = surface_coordinates(rest, surface, pixels)
    rgba = np.rint(sample_coat(skin, uv)).astype(np.uint8)
    return Image.frombytes("RGBa", size, rgba.tobytes()).convert("RGBA")


def sprite(genome, adult, pixels_per_cell):
    model = body_surface(genome, adult)
    cols, rows = (2, 4) if adult else (3, 1)
    size = cols*pixels_per_cell, rows*pixels_per_cell
    scale = min((size[0]-.06*pixels_per_cell)/model.width,
                (size[1]-.05*pixels_per_cell)/model.height)
    shown = model.resize(tuple(round(n*scale) for n in model.size), Image.Resampling.NEAREST)
    result = Image.new("RGBA", size)
    result.alpha_composite(shown, ((size[0]-shown.width)//2, (size[1]-shown.height)//2))
    return result


def make_preview():
    image = Image.new("RGB",(990,680),"#192226")
    draw = ImageDraw.Draw(image)
    draw.text((42,22),"橘虎斑 · 体型对照",font=font(25),fill="#E7E7DC")
    cell = 86
    studies = {}
    for name,allele in (("清瘦",0),("适中",128),("圆胖",255)):
        chromosome = ((allele,)*3,)*2
        studies[name] = Genome(PARENT_A.pairs[:3]+(chromosome,))
    for i,(name,genome) in enumerate(studies.items()):
        cx = 176 + i*319
        draw.text((cx,90),name,anchor="mm",font=font(20),fill="#DEE5DE")
        for adult,y in ((True,119),(False,506)):
            cols,rows = (2,4) if adult else (3,1)
            w,h = cols*cell,rows*cell
            x = cx-w//2
            draw.rectangle((x,y,x+w,y+h),fill="#222D31",outline="#465457")
            for j in range(1,cols):
                draw.line((x+j*cell,y,x+j*cell,y+h),fill="#39464A")
            for j in range(1,rows):
                draw.line((x,y+j*cell,x+w,y+j*cell),fill="#39464A")
            cat = sprite(genome,adult,cell)
            image.paste(cat,(x,y),cat)
    draw.text((42,630),"上排  成猫坐姿  2 × 4",font=font(16),fill="#BBC9C6")
    draw.text((527,630),"下排  幼猫趴姿  3 × 1",font=font(16),fill="#BBC9C6")
    image.save(ROOT/"docs"/"cat-body-study.png")

    # Bring the two ages face to face in a detail view of the full endpoint.
    portrait = Image.new("RGB", (796, 528), "#263031")
    floor = 486
    for adult, left in ((False, 36), (True, 420)):
        cat = body_surface(studies["圆胖"], adult)
        cat = cat.crop(cat.getbbox())
        cat = cat.resize((cat.width*2, cat.height*2), Image.Resampling.NEAREST)
        portrait.paste(cat, (left, floor-cat.height), cat)
    portrait.save(ROOT/"docs"/"cat-round-study.png")


if __name__ == "__main__":
    make_preview()
