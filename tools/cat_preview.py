"""Draw cats from diploid genomes. Run: python tools/cat_preview.py (Pillow)."""

import argparse
import json
import math
import random
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


CELL = 32
PAPER = (229, 224, 208)
MUTED = (145, 166, 163)
TILE = (35, 48, 53)
GRID = (62, 79, 81)
# Linked loci are ordered on each chromosome. Alleles range from 0 to 255.
CHROMOSOMES = (("warmth", "value", "white"), ("bands", "curl", "phase"),
               ("ear", "face", "eye"), ("frame", "fullness", "jowls"))
DEFAULT_BUILD = ((0, 95, 110), (0, 95, 110))


@dataclass(frozen=True)
class Genome:
    # One maternal and one paternal sequence per chromosome.
    pairs: tuple

    def chromosomes(self):
        # Earlier preview genomes predate the body chromosome.
        return self.pairs + (DEFAULT_BUILD,) if len(self.pairs) == 3 else self.pairs

    def express(self):
        traits = {}
        for names, (left, right) in zip(CHROMOSOMES, self.chromosomes()):
            for name, a, b in zip(names, left, right):
                traits[name] = (a + b) / 510
        return traits

    def gamete(self, rng):
        result = []
        for left, right in self.chromosomes():
            if rng.randrange(2):
                left, right = right, left
            cut = rng.randint(1, len(left))
            result.append(left[:cut] + right[cut:])
        return result

PARENT_A = Genome((((241, 198, 32), (211, 154, 65)),
                   ((210, 177, 30), (166, 145, 70)),
                   ((191, 155, 52), (207, 125, 81)),
                   ((223, 245, 216), (241, 235, 253))))
PARENT_B = Genome((((17, 115, 219), (34, 164, 202)),
                   ((35, 77, 181), (19, 58, 209)),
                   ((96, 214, 219), (82, 204, 235)),
                   ((22, 31, 55), (14, 43, 78))))


def offspring(seed):
    rng = random.Random(seed)
    return Genome(tuple(zip(PARENT_A.gamete(rng), PARENT_B.gamete(rng))))


def mix(a, b, amount):
    return tuple(round(x + (y - x) * amount) for x, y in zip(a, b))


# Each pose is one anatomical model, expressed as vector contours in body
# coordinates. The planes describe linework and light, not a cat's coat.
@lru_cache(maxsize=1)
def poses():
    return json.loads(Path(__file__).with_name("cat_pose_contours.json").read_text("utf-8"))


def ellipse(u, v, cx, cy, rx, ry):
    return ((u-cx)/rx)**2 + ((v-cy)/ry)**2


def smooth(t):
    t = max(0., min(1., t))
    return t*t*(3-2*t)


class Coat:
    def __init__(self, traits):
        self.t = traits
        warmth = traits["warmth"]
        self.base = mix((132,155,168), (241,149,67), warmth)
        self.base = mix(self.base, (240,208,172), .14*traits["value"])
        self.mark = mix((79,92,110), (171,91,48), warmth)
        self.ivory = mix((237,237,220), (255,229,182), warmth)
        self.eye = mix((132,199,106), (237,181,65), traits["eye"])

    def pattern(self, u, v, adult):
        t = self.t
        # Continuous fields on the body. Taper each band towards the belly,
        # bend it around the haunch and break it at the shoulder.
        if adult:
            head = v < .31 and u < .57
            lateral = .40 < u and .31 < v < .88
            arc = u + .18*(.63-v)**2 + .27*math.sin(5*v)
            edge = .73 - .25*math.cos(4*v)
            depth = (u-.47)/max(.16,edge-.41)
            tail = .08 < u < .97 and v > .964-.045*(2*u-1)**2
            bib = ellipse(u,v,.22,.37,.055+.17*t["white"],.085+.30*t["white"])
            socks = min(ellipse(u,v,.307,.887,.034+.039*t["white"],.010+.033*t["white"]),
                        ellipse(u,v,.466,.895,.037+.064*t["white"],.012+.035*t["white"])) < 1
            chin = ellipse(u,v,.09,.242,.055+.093*t["white"],.037+.029*t["white"])
            on_face = ellipse(u,v,.29,.142,.31,.20)
            cheek = ellipse(u,v,.37,.215,.14,.073)
            face_phase = u*7.1 - v*(2.3+t["curl"])
        else:
            head = u > .69 and v < .80
            lateral = .28 < u < .74 and v > .31
            arc = u + (.04+.07*t["curl"])*math.sin(5*v+t["phase"])
            depth = 1.4-v
            tail = u < .32 and v>.59
            bib = ellipse(u,v,.82,.75,.050+.062*t["white"],.12+.15*t["white"])
            socks = min(ellipse(u,v,.44,.939,.036+.04*t["white"],.09),
                        ellipse(u,v,.89,.933,.05+.05*t["white"],.07)) < 1
            chin = ellipse(u,v,.94,.68,.025+.061*t["white"],.06+.06*t["white"])
            on_face = ellipse(u,v,.83,.39,.145,.4)
            cheek = ellipse(u,v,.845,.6,.075,.12)
            face_phase = u*15 + v*(.40+t["curl"])
        phase = t["phase"]*.55
        frequency = 3.8+6*t["bands"]
        wave = math.cos(2*math.pi*(arc*frequency+phase))
        stripe = lateral and depth > .12 and wave > 1.06-.75*smooth(depth)
        if head:
            stripe = (on_face<1 and v < (.15 if adult else .45) and
                      math.cos(2*math.pi*(face_phase+phase))>.55) or (
                      cheek<1 and math.cos((v*(110 if adult else 33)+u*8))>.54+.34*cheek)
        if tail:
            stripe = math.cos(2*math.pi*(u*frequency + phase + v*.8))>.42
        white = bib<1 or chin<1 or socks
        return self.ivory if white else (self.mark if stripe else self.base)

    def ramp(self, pigment):
        shade = mix((49,56,73),(103,57,55),self.t["warmth"])
        return (mix(shade,(38,35,47),.45),mix(pigment,shade,.78),
                mix(pigment,shade,.50),mix(pigment,shade,.20),pigment,
                mix(pigment,(255,224,174),.41),mix(pigment,(255,241,203),.72))


@lru_cache(maxsize=2)
def pose_planes(adult):
    pose = poses()["adult" if adult else "kitten"]
    return paint_planes(pose, tuple(pose["size"]))


def paint_planes(pose, size, project=None):
    planes = Image.new("L", size, 255)
    materials = ("outline","deep","shade","mid","lit","sun","glint","inner_ear",
                 "iris","dark_iris","nose","eye_light")
    for index, material in enumerate(materials):
        mask = Image.new("1",planes.size)
        draw = ImageDraw.Draw(mask)
        for filled,coords in pose["planes"][material]:
            points = project(coords) if project else coords
            draw.polygon(points,fill=filled)
        planes.paste(index, mask=mask)

    return planes


def bell(value, centre, radius):
    return math.exp(-((value-centre)/radius)**2)


def render(genome, adult, pixels_per_cell=CELL):
    # Fixed pose-space projection. Neither an individual's genome nor its
    # silhouette bounds determine the scale, gaze direction or paw positions.
    traits = genome.express()
    coat = Coat(traits)
    planes = pose_planes(adult)
    width, height = planes.size
    plane = planes.load()
    cells = (2,4) if adult else (3,1)
    size = tuple(n*pixels_per_cell for n in cells)
    # Rasterise once above native size for antialiasing at inventory scale.
    resolution = max(2*pixels_per_cell, 96)
    model = Image.new("RGBA", tuple(n*resolution for n in cells))
    pixels = model.load()
    ramps = {p:coat.ramp(p) for p in (coat.base,coat.mark,coat.ivory)}
    thickness = .68*traits["fullness"] + .32*traits["frame"]
    cheeks = .64*traits["jowls"] + .36*thickness
    # The inventory rectangle sets the canvas size. Use a single scale for
    # both axes of the reference pose so its skull, spine and limbs retain
    # their proportions regardless of the inventory rectangle's aspect ratio.
    scale_x = 1.52 if adult else 2.43
    scale_y = scale_x * height / width
    # Leave room behind the adult's haunch at maximum fullness.
    origin_x = (cells[0] - scale_x) / 2 - (.045 if adult else 0)
    origin_y = (cells[1] - scale_y) / 2

    for y in range(model.height):
        v = ((y+.5)/resolution-origin_y)/scale_y
        # The ribcage, belly and haunch grow at different heights. The legs
        # taper back to the fixed paw line and the ear/eye region stays rigid.
        body_zone = smooth((v-.23)/.17)*(1-smooth((v-.78)/.14))
        chest = bell(v,.49,.20)
        hip = bell(v,.70,.24)
        jaw = bell(v,.28,.13)*smooth((v-.15)/.09)
        skull = 1-smooth((v-.26)/.14)
        for x in range(model.width):
            u = ((x+.5)/resolution-origin_x)/scale_x
            if adult:
                # Inverse-map both sides towards the supporting foreleg.
                # A fuller belly hangs slightly lower between foreleg and hip.
                # Positive scales on either side of this fixed axis keep
                # adjacent pixels ordered, even at maximum width.
                leg_axis = .41
                # In profile the foreleg is in front of the abdomen. Broadening
                # the lower chest as much as the rear thigh bows that leg out.
                if u < leg_axis:
                    bulge = .53*chest
                else:
                    bulge = .15*chest+.38*hip
                su = leg_axis+(u-leg_axis)/(1+thickness*body_zone*bulge)
                su = .28+(su-.28)/(1+.10*skull+.30*cheeks*jaw)
                # Lower the soft outline over the haunch while keeping both
                # paw soles and the wrapped tail at their rest positions.
                sv = v - thickness*.057*bell(u,.70,.23)*bell(v,.72,.15)*body_zone
                sv = .18+(sv-.18)/(1+.06*skull)
                sv -= cheeks*.030*jaw*bell(u,.28,.24)
                # A little extra depth below the chin; the nose and eye retain
                # the original reference rather than sliding with the cheeks.
                face_weight = (1-smooth((v-.16)/.18))*bell(u,.31,.33)
                cx = .28
            else:
                # The back rises from the fixed floor. The eye, nose, tail end
                # and outstretched forepaws stay in their original coordinates.
                torso = bell(u,.51,.20)
                sv = v + thickness*.23*torso*(1-smooth((v-.49)/.39))
                cheek = bell(u,.845,.072)*bell(v,.68,.115)
                sv -= .055*cheeks*cheek
                su = u + .095*thickness*bell(u,.34,.09)*bell(v,.59,.24)
                face_weight = (1-smooth((v-.38)/.41))*smooth((u-.68)/.10)
                cx = .85
            su = cx+(su-cx)/(1+face_weight*.13*(traits["face"]-.55))
            sv += face_weight*(.03 if adult else .075)*(traits["ear"]-.5)*(1-v)
            ix, iy = round(su*(width-1)), round(sv*(height-1))
            if not (0<=ix<width and 0<=iy<height):
                continue
            value = plane[ix,iy]
            if value == 255:
                continue
            if value < 7:
                color = ramps[coat.pattern(su,sv,adult)][value]
            elif value == 7:
                color = mix((164,84,84),(225,133,119),smooth(1-sv*3))
            elif value in (8,9):
                color = coat.eye if value == 8 else mix(coat.eye,(31,36,42),.60)
            elif value == 10:
                color = (159,85,76)
            else:
                color = (255,240,206)
            pixels[x,y] = (*color,255)

    return model.resize(size, Image.Resampling.LANCZOS)


def font(size):
    for path in ("C:/Windows/Fonts/msyh.ttc", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(path,size)
        except OSError:
            continue
    return ImageFont.load_default()


def preview(seed, output):
    image = Image.new("RGB", (1056, 764), (22, 30, 35))
    d = ImageDraw.Draw(image)
    d.text((49, 25), "深空猫舍", font=font(26), fill=PAPER)
    d.line((50, 77, 1006, 77), fill=GRID)
    family = (("亲本 A · 圆胖", PARENT_A), ("亲本 B · 清瘦", PARENT_B), ("子代", offspring(seed)))
    for index, (name, genome) in enumerate(family):
        cx = 204 + 324*index
        d.text((cx, 110), name, anchor="mm", font=font(20), fill=PAPER)
        for adult, y in ((True, 144), (False, 578)):
            sprite = render(genome, adult, 2*CELL)
            w,h = (n//2 for n in sprite.size)
            zoom = 3
            x = cx - w*zoom//2
            right, bottom = x+w*zoom, y+h*zoom
            d.rectangle((x,y,right,bottom), fill=TILE, outline=GRID)
            for dx in range(CELL,w,CELL):
                d.line((x+zoom*dx,y,x+zoom*dx,bottom), fill=GRID)
            for dy in range(CELL,h,CELL):
                d.line((x,y+zoom*dy,right,y+zoom*dy), fill=GRID)
            shown = sprite.resize((w*zoom,h*zoom), Image.Resampling.LANCZOS)
            image.paste(shown,(x,y),shown)
    d.line((50, 710, 1006, 710), fill=GRID)
    d.text((50, 723), "上排   成猫坐姿  2 × 4", font=font(16), fill=MUTED)
    d.text((525, 723), "下排   同一基因的幼猫趴姿  3 × 1", font=font(16), fill=MUTED)
    output.parent.mkdir(parents=True,exist_ok=True)
    image.save(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seed",type=int,default=23,help="offspring inheritance seed")
    parser.add_argument("--output",type=Path,default=Path(__file__).resolve().parents[1]/"docs"/"cat-genetics-preview.png")
    args = parser.parse_args()
    preview(args.seed,args.output)
