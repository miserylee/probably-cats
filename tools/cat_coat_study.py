"""Orange tabby coat on the approved rest poses. Run with Python and Pillow."""

import math

from PIL import Image, ImageDraw

from cat_preview import Coat, PARENT_A, font, mix, paint_planes, pose_planes, poses
from cat_skeleton import ADULT, KITTEN, CHAINS, ROOT


def ribbon(draw, start, control, end, width, color):
    """Paint a gently curved marking which narrows to a point."""
    left, right = [], []
    for i in range(25):
        t = i / 24
        s = 1 - t
        x = s*s*start[0] + 2*s*t*control[0] + t*t*end[0]
        y = s*s*start[1] + 2*s*t*control[1] + t*t*end[1]
        dx = s*(control[0]-start[0]) + t*(end[0]-control[0])
        dy = s*(control[1]-start[1]) + t*(end[1]-control[1])
        length = math.hypot(dx, dy) or 1
        r = width * (1-t**1.5) * (.73+.27*math.sin(math.pi*t))
        nx, ny = -dy*r/length, dx*r/length
        left.append((x+nx, y+ny))
        right.append((x-nx, y-ny))
    draw.polygon(left + right[::-1], fill=color)


def path_point(points, progress):
    lengths = [math.dist(a,b) for a,b in zip(points,points[1:])]
    distance = max(0,min(1,progress))*sum(lengths)
    for a,b,length in zip(points,points[1:],lengths):
        if distance <= length:
            t = distance/length
            return (a[0]+t*(b[0]-a[0]),a[1]+t*(b[1]-a[1]))
        distance -= length
    return points[-1]


def soft_face(skin, coat, adult, strength):
    """Paint the cheek, whisker follicles and eye together in face coordinates."""
    cx, cy = (27, 39) if adult else (180, 36)
    side = 1 if adult else -1
    unit = 1 if adult else .83
    scale = 4
    radius = 36
    layer = Image.new("RGBA", (2*radius*scale, 2*radius*scale))
    draw = ImageDraw.Draw(layer)

    def point(x, y):
        return ((radius+side*x*unit)*scale, (radius+y*unit)*scale)

    def curve(a, b, c, d):
        path = []
        for i in range(17):
            t = i/16
            s = 1-t
            path.append(point(*(s**3*p + 3*s*s*t*q + 3*s*t*t*r + t**3*u
                                for p,q,r,u in zip(a,b,c,d))))
        return path

    def oval(x, y, rx, ry, color):
        px, py = point(x,y)
        rx, ry = rx*unit*scale, ry*unit*scale
        draw.ellipse((px-rx,py-ry,px+rx,py+ry), fill=color)

    # A narrow, warm lower edge separates the cheek from the chest bib.
    cheek = curve((-7,21), (2,24), (14,23), (21,18))
    cheek += curve((21,18), (15,25), (1,27), (-7,21))
    draw.polygon(cheek, fill=(*mix(coat.base,(150,86,49),.45),148))
    pad = curve((-22,12),(-16,8),(-7,9),(-2,12))
    pad += curve((-2,12),(4,16),(12,18),(18,16))
    pad += curve((18,16),(11,22),(0,24),(-9,21))
    pad += curve((-9,21),(-17,20),(-21,17),(-22,12))
    draw.polygon(pad, fill=(*mix(coat.base,coat.ivory,.64),146))
    # The whisker pad turns over above a short mouth line in profile.
    mouth = curve((-21,14),(-20,17),(-16,18.5),(-12,18))
    mouth += curve((-12,18),(-9,18),(-7,17),(-6,16))
    draw.line(mouth, fill=(*mix(coat.base,(122,80,68),.7),180),width=3)
    oval(-21.5,9.3,1.7,1, (222,153,143))
    for x,y in ((-14,13),(-10,14.5),(-13,15.5)):
        oval(x,y,.55,.4, (*mix(coat.base,(130,84,67),.65),125))

    # An oblique upper lid, round pupil and two differently sized reflections
    # keep the gaze legible even when the inventory thumbnail is small.
    lid = curve((-9,3),(-8,-2),(-4,-6.5),(2,-6))
    lid += curve((2,-6),(7,-6),(9,-3),(12,-2))
    lid += curve((12,-2),(9,-1),(8,5),(3,6))
    lid += curve((3,6),(-2,8),(-7,7),(-9,3))
    draw.polygon(lid, fill=mix(coat.mark,(53,42,40),.78))
    iris = curve((-7,3),(-6,-1),(-3,-4.8),(1,-4.8))
    iris += curve((1,-4.8),(5,-4.8),(7,-2),(7,0))
    iris += curve((7,0),(7,7),(-3,8),(-7,3))
    draw.polygon(iris, fill=coat.eye)
    oval(-.65,.1,4.25,4.5, mix(coat.eye,(43,43,44),.85))
    oval(-2.4,-2.2,1.7,1.55,(255,249,224))
    oval(3.2,3,.85,.75,(244,234,185))

    overlay = Image.new("RGBA",skin.size)
    overlay.paste(layer.resize((2*radius,2*radius),Image.Resampling.LANCZOS),
                  (cx-radius,cy-radius))
    mask = overlay.getchannel("A").point(lambda value: round(strength*value))
    result = Image.composite(overlay.convert("RGB"),skin.convert("RGB"),mask)
    result.putalpha(skin.getchannel("A"))
    return result


def pigments(traits, adult, size, project=None):
    coat = Coat(traits)
    base = mix(coat.base, (231,147,66), .10)
    dark = mix(coat.mark,(106,65,47),.24)
    cream = mix(coat.ivory,(255,239,203),.38)
    surface = Image.new("RGB",size,base)
    draw = ImageDraw.Draw(surface)
    def band(a,b,c,width,color):
        if project:
            a,b,c = project(a),project(b),project(c)
        ribbon(draw,a,b,c,width,color)

    def oval(bounds, color):
        x0,y0,x1,y1 = bounds
        if project:
            # Keep the muzzle and toe markings registered to their landmarks.
            cx,cy = (x0+x1)/2,(y0+y1)/2
            x,y = project((cx,cy))
            bounds = x0+x-cx,y0+y-cy,x1+x-cx,y1+y-cy
        draw.ellipse(bounds,fill=color)

    rig = ADULT if adult else KITTEN
    count = 3+round(4*traits["bands"])
    curve = traits["curl"]
    spine = [rig[j] for j in ("withers","back","loin","sacrum")]
    for i in range(count):
        t = (i+.18+.35*traits["phase"])/count
        x,y = path_point(spine,t)
        spread = math.sin(math.pi*t)
        if adult:
            start = (x+8,y-9)
            control = (x-3-7*curve,y+7)
            end = (x+1-8*curve,y+10+15*spread)
        else:
            start = (x,y-9)
            control = (x+2+6*curve,y+2)
            end = (x-4+3*curve,y+7+9*spread)
        band(start,control,end,1.7+1.2*spread,dark)

    tail = [rig[j] for j in CHAINS["tail"].split()[1:]]
    bands = 3+round(traits["bands"]*3)
    for i in range(bands):
        t = .29+(i+.27*traits["phase"])*(.64/bands)
        x,y = path_point(tail,t)
        nx,ny = path_point(tail,t+.015)
        length = math.hypot(nx-x,ny-y) or 1
        px,py = -(ny-y)/length,(nx-x)/length
        radius = 4.3 if adult else 2.9
        band((x-px*radius,y-py*radius),(x+1,y),
               (x+px*radius,y+py*radius),1.8,dark)

    if adult:
        # The throat patch narrows as it follows the chest down to the foreleg.
        reach = 7+25*traits["white"]
        band((18,60),(47,73),(27,104+reach),8+7*traits["white"],cream)
        oval((3,49,30,61),cream)
        face_marks = [((34,28),(27,29),(22,34)),((43,26),(35,26),(30,32)),
                      ((61,41),(52,45),(41,44)),((60,46),(50,52),(38,50))]
    else:
        oval((177,43,198,53),cream)
        band((177,50),(170,54),(160-12*traits["white"],57),3.7,cream)
        face_marks = [((165,25),(173,25),(176,30)),((171,23),(178,23),(181,29)),
                      ((153,39),(160,44),(171,42)),((154,44),(162,49),(172,45))]
    for a,b,c in face_marks:
        band(a,b,c,1.1+.6*traits["bands"],dark)
    for key in ("forepaw","hindpaw"):
        x,y = rig[key]
        rx = (5.5 if adult else 4)+8*traits["white"]
        oval((x-rx,y-2-5*traits["white"],x+rx,y+4),cream)
    return surface


def render_skin(genome, adult, *, project=None, canvas=None):
    stage = "adult" if adult else "kitten"
    pose = poses()[stage]
    traits = genome.express()
    coat = Coat(traits)
    def project_polygon(coords):
        return [project(point) for point in zip(coords[::2],coords[1::2])]

    planes = paint_planes(pose,canvas,project_polygon) if project else pose_planes(adult)
    paint = pigments(traits,adult,planes.size,project)
    light = [(40,36,46),(106,62,53),(144,81,57)]
    ramps = {}
    for _, pigment in paint.getcolors():
        ramps[pigment] = [mix(pigment,light[0],.86),mix(pigment,light[1],.76),
                          mix(pigment,light[2],.48),mix(pigment,light[2],.18),
                          pigment,mix(pigment,(255,215,152),.43),
                          mix(pigment,(255,234,194),.72)]
    details = {7:(215,131,116),8:coat.eye,9:mix(coat.eye,(30,43,34),.74),
               10:(141,76,73),11:(255,249,224)}
    image = Image.new("RGBA",planes.size)
    mask = Image.new("L",planes.size)
    draw = ImageDraw.Draw(mask)
    for filled,points in pose["planes"]["outline"]:
        draw.polygon(project_polygon(points) if project else points,fill=255*filled)
    for y in range(image.height):
        for x in range(image.width):
            if mask.getpixel((x,y)):
                tone = planes.getpixel((x,y))
                color = ramps[paint.getpixel((x,y))][tone] if tone<7 else details[tone]
                image.putpixel((x,y),(*color,255))
    return image


def make_preview():
    image = Image.new("RGB",(760,540),"#192226")
    draw = ImageDraw.Draw(image)
    draw.text((36,24),"橘虎斑",font=font(25),fill="#E7E7DC")
    cell = 98
    floor = 462
    for adult, cx, label in ((False,205,"幼猫  ·  3 × 1"),(True,561,"成猫  ·  2 × 4")):
        cols,rows = (2,4) if adult else (3,1)
        w,h = cols*cell,rows*cell
        x,y = cx-w//2,floor-h
        draw.rectangle((x,y,x+w,y+h),fill="#222D31",outline="#465457")
        for i in range(1,cols):
            draw.line((x+i*cell,y,x+i*cell,floor),fill="#39464A")
        for i in range(1,rows):
            draw.line((x,y+i*cell,x+w,y+i*cell),fill="#39464A")
        sprite = render_skin(PARENT_A,adult)
        # Same proportional fit and padding as the approved skeleton sheet.
        scale = min((w-12*cell/94)/sprite.width,(h-10*cell/94)/sprite.height)
        sprite = sprite.resize((round(sprite.width*scale),round(sprite.height*scale)),
                               Image.Resampling.NEAREST)
        image.paste(sprite,(cx-sprite.width//2,y+(h-sprite.height)//2),sprite)
        draw.text((cx,floor+28),label,font=font(17),anchor="mm",fill="#C1CFCC")
    image.save(ROOT/"docs"/"cat-coat-study.png")


if __name__ == "__main__":
    make_preview()
