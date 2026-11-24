"""Fit the seated adult's painted features to the prone kitten's UV layout."""

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from pack_cat_paint import ART, extend_edge


# Each correspondence identifies the same feature in the adult and kitten
# paintings (adult X/Y, kitten X/Y). The kitten has a shorter muzzle and a
# taller eyelid opening. The dark ink line itself comes from the adult.
FRONT = [
    # Outer and inner ear rims, pedicles, and crown.
    (196,44,194,144),(204,56,209,153),(182,102,169,207),(179,149,169,258),
    (191,214,194,329),(236,79,259,163),(299,134,327,200),
    (203,65,193,158),(260,142,280,215),(204,182,199,284),
    (351,126,392,192),(378,111,452,193),(447,18,574,141),
    (468,59,593,172),(485,124,604,228),(485,185,584,328),
    (445,37,569,157),(397,126,491,211),(465,158,568,291),
    # Brow, both canthi, upper and lower lids, iris.
    (228,215,258,301),(267,190,300,272),(301,196,343,280),
    (219,241,252,340),(264,230,301,321),(290,237,329,324),
    (319,270,357,372),(271,271,310,378),(242,259,278,363),(285,250,320,350),
    (382,182,442,272),(409,172,472,260),(453,184,517,281),
    (385,260,432,374),(405,216,450,322),(438,207,482,312),
    (474,207,533,325),(438,245,490,365),(405,256,455,378),(416,233,467,345),
    # Nose leather, mouth corners, whisker pads and cheek tufts.
    (336,289,368,403),(357,282,395,394),(378,287,423,403),(359,305,395,425),
    (359,321,395,445),(319,326,340,448),(340,333,364,456),
    (381,328,423,456),(399,317,450,444),
    (315,306,340,420),(315,315,340,432),(248,322,263,439),(242,344,257,466),
    (399,296,448,421),(399,305,448,432),(456,298,514,425),(474,329,548,481),
    (152,296,160,379),(138,309,154,389),(183,350,217,464),
    (237,361,270,481),(266,369,307,494),(357,366,395,505),
    (448,350,489,487),(507,320,568,452),(560,252,622,382),(539,241,614,369),
    (206,383,216,492),(509,359,568,500)
]
PROFILE = [
    (955,42,1034,133),(944,150,1016,218),(1004,128,1110,197),
    (1046,22,1168,127),(1001,132,1104,217),(1077,68,1201,162),
    (1129,175,1270,252),(1145,200,1290,294),(1191,298,1303,425),
    (1020,138,1121,228),(1045,43,1167,147),(1064,166,1194,305),
    (895,197,963,270),(924,175,1003,253),(969,192,1063,280),
    (902,261,969,377),(917,224,986,321),(941,214,1010,304),
    (980,216,1063,333),(948,246,1032,363),(920,257,994,376),(929,236,1009,341),
    (879,250,945,352),(891,213,952,300),(921,164,980,241),
    (843,273,887,378),(866,276,919,386),(850,298,895,408),
    (868,322,918,447),(891,315,955,433),(857,328,903,447),
    (889,295,949,413),(890,303,949,425),(936,301,995,421),(952,336,1012,463),
    (880,348,939,479),(926,362,1003,495),(1008,373,1110,503),
    (1030,344,1184,494),(1094,345,1233,473),(1157,318,1267,451),
    (1021,251,1125,374),(1065,269,1168,407),(1130,283,1246,411)
]

FRONT_MASK = [
    (144,106),(640,106),(641,402),(616,414),(593,437),(571,457),(544,472),
    (506,484),(465,494),(426,502),(393,509),(353,504),(311,497),(267,486),
    (234,475),(205,460),(175,434),(146,408)
]
PROFILE_MASK = [
    (858,102),(1320,102),(1320,404),(1305,424),(1281,442),(1257,454),
    (1225,474),(1183,491),(1142,501),(1110,508),(1072,502),(1044,500),
    (1008,495),(959,491),(922,481),(888,457),(856,414)
]


def spline_grid(pairs, box):
    """Inverse thin-plate spline; regularize in units of the portrait width."""
    controls = np.asarray(pairs, dtype=float)
    adult, kitten = controls[:, :2], controls[:, 2:]
    origin = kitten.mean(axis=0)
    scale = 460.0
    points = (kitten-origin)/scale

    def distances(samples):
        squared = np.sum((samples[:, None, :] - points[None, :, :])**2, axis=2)
        return squared*np.log(np.maximum(squared, 1e-14))

    affine = np.column_stack((np.ones(len(points)), points))
    kernel = distances(points) + np.eye(len(points))*.000035
    system = np.block([[kernel, affine], [affine.T, np.zeros((3,3))]])
    weights = np.linalg.solve(system, np.vstack((adult, np.zeros((3,2)))))
    left, top, right, bottom = box
    step = 3
    gx = np.arange(left, right+step, step)
    gy = np.arange(top, bottom+step, step)
    uv = np.stack(np.meshgrid(gx, gy), axis=-1).reshape(-1,2)
    uv = (uv-origin)/scale
    predicted = distances(uv)@weights[:-3] + weights[-3] + uv@weights[-2:]
    # Bivariate linear interpolation of this smooth coordinate field.
    mapped = []
    for grid in predicted.T:
        grid = grid.reshape(len(gy), len(gx))
        across = np.stack([np.interp(np.arange(left,right),gx,row) for row in grid])
        mapped.append(np.stack([np.interp(np.arange(top,bottom),gy,col) for col in across.T],axis=1))
    return mapped


def pigment_mask(painting):
    rgb = np.asarray(painting,dtype=float)
    r,g,b = np.moveaxis(rgb,-1,0)
    backdrop = (r>155) & (b>155) & (r>g*1.06) & (b>g*1.06)
    return Image.fromarray((~backdrop).astype(np.uint8)*255)


def fit_face(source, target, pose, landmarks, perimeter):
    mask = Image.new("L",target.size)
    ImageDraw.Draw(mask).polygon(perimeter,fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(1.8))
    box = mask.getbbox()
    coverage = np.asarray(mask.crop(box),dtype=float)*np.asarray(pose.crop(box))/255
    sx,sy = spline_grid(landmarks,box)
    sx=np.clip(sx,0,source.shape[1]-1.001)
    sy=np.clip(sy,0,source.shape[0]-1.001)
    x,y = sx.astype(int),sy.astype(int)
    u,v = (sx-x)[...,None],(sy-y)[...,None]
    rgb = ((1-u)*(1-v)*source[y,x] + u*(1-v)*source[y,x+1]
           + (1-u)*v*source[y+1,x] + u*v*source[y+1,x+1])
    target.paste(Image.fromarray(np.uint8(rgb)),box[:2],Image.fromarray(np.uint8(coverage)))


def kitten_paint(adult, pose, body):
    source = np.asarray(extend_edge(adult,pigment_mask(adult)))
    silhouette = pigment_mask(pose)
    target = body.copy()
    fit_face(source,target,silhouette,FRONT,FRONT_MASK)
    fit_face(source,target,silhouette,PROFILE,PROFILE_MASK)
    return target


if __name__=="__main__":
    from kitten_body_paint import body_paint
    adult = Image.open(ART/"calico-adult.webp").convert("RGB")
    pose = Image.open(ART/"kitten-pose.webp").convert("RGB")
    kitten_paint(adult,pose,body_paint(adult,pose)).save(ART/"calico-kitten.webp","WEBP",quality=95,method=6)
