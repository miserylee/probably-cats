"""Register the seated cat's paint to the separate prone kitten construction."""

import numpy as np
from PIL import Image, ImageDraw, ImageFilter


def _chroma(image):
    red, green, blue = np.moveaxis(image, -1, 0)
    return (red > 145) & (blue > 150) & (blue > 1.06 * green) & (red > 1.06 * green)


def _polygon(size, points, feather=1.4):
    mask = Image.new("L", size)
    ImageDraw.Draw(mask).polygon(points, fill=255)
    return np.asarray(mask.filter(ImageFilter.GaussianBlur(feather)), dtype=np.float32) / 255


def _sample(image, x, y):
    """Bilinear lookup in raw painting coordinates."""
    x = np.clip(x, 0, image.shape[1] - 1.001)
    y = np.clip(y, 0, image.shape[0] - 1.001)
    i = x.astype(int)
    j = y.astype(int)
    dx = (x - i)[..., None]
    dy = (y - j)[..., None]
    return ((1-dy) * (image[j, i] * (1-dx) + image[j, i+1] * dx) +
            dy * (image[j+1, i] * (1-dx) + image[j+1, i+1] * dx))


def body_paint(adult, kitten):
    """Return an RGB kitten study with the elder cat's anatomically registered paint."""
    if adult.size != (1536, 1024) or kitten.size != (2172, 724):
        raise ValueError("Expected the 1536 × 1024 adult and 2172 × 724 kitten studies.")
    src = np.asarray(adult.convert("RGB"), dtype=np.float32)
    original = np.asarray(kitten.convert("RGB"), dtype=np.float32)
    out = original.copy()
    inside = ~_chroma(original)
    size = kitten.size

    def ribbon(columns, region=None, retain=.035, preserve_toes=False):
        # Each vertical strip has upper and lower landmarks in both animals.
        # Linear interpolation across neighboring strips preserves painted edges.
        cols = np.array(columns, dtype=float)
        left, right = int(cols[0, 0]), int(cols[-1, 0]) + 1
        top, bottom = int(min(cols[:, 1])), int(max(cols[:, 2])) + 1
        yy, xx = np.ogrid[top:bottom, left:right]
        upper, lower, au, av, bu, bv = (np.interp(xx[0], cols[:, 0], c) for c in cols.T[1:])
        t = np.clip((yy - upper) / np.maximum(1, lower - upper), 0, 1)
        sx = au + (bu - au) * t
        sy = av + (bv - av) * t
        paint = _sample(src, sx, sy)
        mask = (inside[top:bottom, left:right] * np.clip(yy-upper, 0, 1) *
                np.clip(lower-yy, 0, 1) * (~_chroma(paint)))
        if region is not None:
            mask *= region[top:bottom, left:right]
        area = out[top:bottom, left:right]
        # A little local modeling from the kitten retains the soft joints and toe pads.
        native = original[top:bottom, left:right]
        if preserve_toes:
            # The adult has narrower individual toes. Keep the spacing of the
            # kitten's toe creases while transferring the sampled foot pigment.
            weights = np.array([.299, .587, .114])
            delta = native @ weights - paint @ weights
            paint = paint + delta[...,None]*np.array([1.04,.99,.92])
        paint = paint*(1-retain) + native*retain
        area += (paint-area) * mask[..., None]

    # Neck, breast and haunch follow continuous arcs, from the high neck of
    # the seated study to the lower shoulder and tucked pelvis of the kitten.
    front = [
        (116,520,661,152,736,195,877), (145,484,661,165,680,209,895),
        (180,457,662,207,539,225,892), (221,446,663,239,453,257,865),
        (258,458,663,268,393,288,771), (307,478,664,301,374,313,716),
        (355,491,664,331,375,329,692), (393,495,665,352,367,340,699),
        (431,492,665,381,374,359,692), (480,478,665,417,379,383,711),
        (529,456,663,451,405,426,786), (563,443,663,466,450,461,830),
        (606,467,663,474,523,498,838), (639,500,663,495,610,519,835),
        (664,559,662,510,680,525,821),
    ]
    front_body = _polygon(size,[(102,672),(102,445),(208,443),(246,459),(292,477),
        (345,494),(385,503),(408,503),(457,495),(501,480),(547,458),(577,442),
        (627,444),(680,512),(680,673)])
    ribbon(front, front_body)
    red, green, blue = np.moveaxis(original, -1, 0)
    warm = np.clip((red-blue-48)/31, 0, 1)
    cream = np.clip((green-.66*red+1)/11, 0, 1)*np.clip((red-141)/28,0,1)
    x = np.arange(size[0])
    shoulder_edge = np.maximum(np.clip((294-x)/52,0,1),np.clip((x-480)/52,0,1))
    shoulder = front_body*warm*(1-cream)*shoulder_edge
    ribbon([(115,443,651,1281,586,1263,668),(155,443,651,1234,542,1232,673),
            (185,443,651,1179,515,1194,673),(221,443,651,1146,484,1150,671),
            (255,443,651,1109,455,1108,659),(294,443,651,1086,444,1080,637)],shoulder)
    ribbon([(480,443,651,1086,444,1080,637),(524,443,651,1109,455,1108,659),
            (563,443,651,1146,484,1150,671),(598,443,651,1179,515,1194,673),
            (631,443,651,1234,542,1232,673),(665,443,651,1281,586,1263,668)],shoulder)
    side = [
        (1030,507,661,937,390,928,573), (1061,506,661,961,376,940,611),
        (1110,504,661,1004,371,963,645), (1170,488,661,1045,370,998,665),
        (1230,459,661,1106,390,1039,683), (1293,407,661,1150,419,1055,707),
        (1345,390,661,1173,459,1070,711), (1400,362,661,1197,480,1089,713),
        (1454,339,662,1230,502,1110,747), (1514,323,662,1269,526,1132,804),
        (1565,327,662,1302,553,1187,845), (1624,342,662,1334,592,1250,846),
        (1687,375,661,1358,641,1310,846), (1733,418,659,1372,693,1341,842),
        (1771,478,656,1384,744,1354,842), (1803,531,654,1387,786,1374,831),
    ]
    side_body = _polygon(size,[(1017,675),(1028,510),(1082,505),(1134,503),
        (1182,492),(1235,466),(1267,448),(1302,427),(1307,392),
        (1430,319),(1579,315),(1700,350),(1811,492),(1811,668)])
    ribbon(side, side_body)

    # Both front stockings use the dark volume of the adult leg. The kitten's
    # original stocking boundaries keep the bent wrists aligned when the mesh turns.
    ink = np.clip((100-(red-blue))/34, 0, 1) * np.clip((150-red)/38, 0, 1)
    def sleeve(points, columns):
        ribbon(columns, ink * _polygon(size, points), .06)
    sleeve([(112,499),(226,505),(313,540),(355,577),(361,653),(112,657)], [
        (112,487,659,1120,736,1110,828),(211,487,659,1155,699,1138,843),
        (270,501,659,1195,689,1186,856),(362,501,659,1270,692,1251,844)])
    sleeve([(424,575),(493,526),(560,514),(660,497),(661,654),(419,651)], [
        (414,501,659,1263,704,1267,844),(480,501,659,1210,678,1205,844),
        (554,501,659,1156,700,1159,844),(666,487,659,1120,734,1132,833)])
    sleeve([(1080,570),(1179,543),(1272,537),(1337,551),(1355,620),(1345,661),(1090,661)], [
        (1071,536,662,1104,765,1116,828),(1146,536,662,1140,708,1164,842),
        (1231,536,662,1210,669,1233,851),(1294,536,662,1264,684,1296,844),
        (1361,536,662,1306,709,1334,827)])

    # Each foot borrows an actual painted paw and preserves the kitten's
    # footprint and perspective. The dark outline between individual toes comes
    # from the corresponding part of the adult study.
    def foot(columns, outline):
        ribbon(columns, _polygon(size, outline, 1.2), .27, preserve_toes=True)

    foot([(198,581,661,265,906,268,953),(237,581,661,280,904,281,959),
          (285,581,661,301,901,301,962),(329,581,661,320,903,321,961),
          (369,581,661,334,911,335,954)],
         [(201,643),(218,620),(235,600),(258,587),(278,582),(302,583),(327,591),
          (348,604),(362,623),(369,644),(361,657),(236,662)])
    foot([(418,581,661,265,907,267,955),(453,581,661,279,902,280,959),
          (500,581,661,303,901,302,962),(540,581,661,322,904,321,960),
          (579,581,661,335,912,334,954)],
         [(420,646),(426,624),(441,605),(458,593),(484,582),(509,582),
          (533,588),(549,599),(565,617),(578,640),(573,654),(544,661),(438,663)])
    foot([(915,583,655,929,926,932,945),(951,583,655,948,909,947,950),
          (986,583,655,966,906,964,953),(1030,583,655,989,913,986,951),
          (1080,583,655,998,918,993,947)],
         [(916,637),(921,610),(939,594),(959,587),(984,582),(1021,583),
          (1063,589),(1082,621),(1058,655),(936,655)])
    foot([(1005,579,662,979,931,982,949),(1057,579,662,992,918,995,952),
          (1104,579,662,1009,916,1011,954),(1144,579,662,1026,919,1029,953),
          (1193,579,662,1040,925,1044,949)],
         [(1005,640),(1018,612),(1043,593),(1080,587),(1117,583),(1136,582),
          (1145,590),(1138,600),(1155,607),(1148,614),(1166,623),(1160,632),
          (1187,648),(1150,659),(1035,662)])
    foot([(1445,581,658,929,930,931,949),(1491,581,658,944,910,945,951),
          (1549,581,658,962,905,964,954),(1612,581,658,981,909,981,952),
          (1692,581,658,997,919,996,949)],
         [(1446,642),(1451,621),(1472,595),(1493,585),(1526,582),(1562,586),
          (1597,598),(1633,614),(1671,633),(1696,649),(1654,657),(1480,659)])

    # The original tail points right. Its painting is the same seated cat's tail
    # sampled from the root on the right to the tip on the left of the adult study.
    tail = [(1730,493,659,1387,813,1361,927),(1763,501,659,1394,818,1361,941),
            (1818,541,659,1379,837,1314,947),(1875,548,659,1347,858,1268,949),
            (1935,546,655,1306,876,1204,949),(1982,550,654,1248,884,1160,951),
            (2025,565,651,1182,881,1121,950),(2069,603,648,1117,891,1062,950)]
    tail_mask=_polygon(size, [(1726,456),(1843,494),(2024,526),(2077,585),
        (2077,658),(1737,663),(1758,638),(1767,598),(1768,566),(1761,537)])
    dark_tail=[(x,at,ab,min(1348,sx),934,min(1355,bx),948)
               for x,at,ab,sx,_,bx,_ in tail]
    ribbon(dark_tail,tail_mask,.10)
    stripe=np.clip((red-blue-38)/45,0,1)
    copper=[(x,at,ab,1210+(x-1730)*.19,885,1227+(x-1730)*.12,912)
            for x,at,ab,*_ in tail]
    ribbon(copper,tail_mask*stripe,.16)
    return Image.fromarray(np.uint8(np.clip(out, 0, 255)))
