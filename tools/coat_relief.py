"""Separate brush relief and anatomical inks from the original calico pigment."""

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


# Eyelids/irises are restored precisely by the runtime eye shader. These
# landmarks protect the other features that pass through every coat color.
FEATURES = {
    "adult": {
        "ears": [[(196,59),(222,79),(270,134),(241,153),(200,191),(188,151),(188,110)],
                 [(445,35),(458,69),(466,113),(466,158),(433,143),(399,131),(421,81)],
                 [(954,58),(979,97),(993,130),(952,148)],
                 [(1045,40),(1058,75),(1064,128),(1064,172),(1018,137)]],
        "noses": [[(334,287),(348,281),(364,280),(379,285),(377,294),(360,307),(353,307),(335,295)],
                  [(842,272),(851,271),(869,275),(870,280),(854,295),(848,297),(841,282)]],
        "brows": [(219,173,310,218),(378,159,462,197),(909,163,980,201)],
        "muzzles": [[(138,309),(257,276),(322,280),(347,303),(383,281),(544,279),
                     (516,352),(510,366),(459,352),(379,373),(264,376),(198,390)],
                    [(838,287),(902,277),(1135,284),(1140,337),(1014,382),(863,357)]],
        "paws": [(197,905,408,967),(917,897,1055,968)]},
    "kitten": {
        "ears": [[(193,155),(230,171),(281,213),(238,254),(199,305),(181,249),(181,210)],
                 [(572,157),(587,204),(586,253),(566,295),(526,251),(489,218),(533,178)],
                 [(1035,144),(1054,166),(1076,199),(1024,215)],
                 [(1167,144),(1192,180),(1206,232),(1195,307),(1168,271),(1122,229)]],
        "noses": [[(364,401),(383,393),(405,392),(425,400),(424,411),(397,428),(385,422),(365,408)],
                  [(885,374),(899,374),(919,382),(924,389),(897,412),(887,407)]],
        "brows": [(253,254,352,304),(432,249,525,292),(976,241,1074,287)],
        "muzzles": [[(147,388),(249,367),(355,382),(392,423),(434,379),(615,375),
                     (607,432),(567,489),(495,502),(394,518),(250,498),(206,494)],
                    [(883,405),(936,388),(1170,388),(1292,436),(1102,512),(930,491)]],
        "paws": [(218,576,579,657),(914,580,1170,656),(1441,580,1710,656)]}
}


def leather_mask(size, stage):
    """Keep ear bowls and nose leather individually addressable on both views."""
    channels = []
    for feature in ("ears", "noses"):
        layer = Image.new("L", size)
        draw = ImageDraw.Draw(layer)
        for outline in FEATURES[stage][feature]:
            draw.polygon(outline, fill=255)
        channels.append(layer.filter(ImageFilter.GaussianBlur(.65)))
    return Image.merge("RGB", (*channels, Image.new("L", size)))


def relief_map(image, stage):
    rgb = np.asarray(image, dtype=np.float32)/255
    r,g,b = np.moveaxis(rgb,-1,0)
    lum = r*.299 + g*.587 + b*.114
    def blur(field,radius):
        channel=Image.fromarray(np.uint8(np.clip(field*255,0,255)))
        return np.asarray(channel.filter(ImageFilter.GaussianBlur(radius)),dtype=np.float32)/255
    def mask(shapes,rectangles=False):
        layer=Image.new("L", image.size)
        draw=ImageDraw.Draw(layer)
        for shape in shapes:
            (draw.rectangle if rectangles else draw.polygon)(shape,fill=255)
        return np.asarray(layer.filter(ImageFilter.GaussianBlur(.8)),dtype=np.float32)/255

    # Average only nearby paint of the same pigment. A black patch beside a
    # white one should leave no bright edge after a solid coat is selected.
    ink=(b>g*.98)&(r<.59)&(r<1.65*b)
    ivory=(g>r*.68)&(b>r*.43)&(r>.43)&~ink
    grain=np.ones_like(lum)
    for pixels,mid in [(ink,.18),(ivory,.80),(~(ink|ivory),.46)]:
        weight=blur(pixels.astype(float),5)
        local=blur(lum*pixels,5)/np.maximum(weight,.012)
        ratio=(lum+.045)/(local+.045)
        # Compress the large differences between pigment families, but keep
        # the broad painted facets and grain *within* each family.
        value=np.clip((lum[pixels]-mid)/(mid+.075),-.44,.40)
        grain[pixels]=np.clip(1 + .41*(ratio[pixels]-1) + .42*value,.66,1.23)
    features=FEATURES[stage]
    protection=mask(features["ears"])
    protection=np.maximum(protection,mask(features["noses"]))
    brown=np.clip((r-b-.018)*32,0,1)
    dark=np.clip((.205-lum)*24,0,1)
    stroke=np.clip((blur(lum,4.5)-lum-.017)*12,0,1)
    protection=np.maximum(protection,mask(features["brows"],True)*dark*brown)
    protection=np.maximum(protection,mask(features["muzzles"])*np.maximum(stroke,dark)*brown*
                          np.clip((.57-lum)*8,0,1))
    footlines=np.clip((blur(lum,4)-lum-.055)*5,0,.82)
    grain*=1-.58*footlines*mask(features["paws"],True)
    packed=np.stack((np.clip((grain-.60)/.8,0,1),protection),axis=-1)
    return Image.fromarray(np.uint8(packed*255),"LA")
