"""Capture the existing cat model's coat-space coordinates for the game's CPU renderer."""
from os import environ
from pathlib import Path
from runpy import run_path

from PIL import Image
from playwright.sync_api import sync_playwright


BAKER = run_path(str(Path(__file__).with_name("bake-cat-portraits.py")))

FIELDS = """
    uniform float portraitCoatField;
    vec3 portraitFinish(vec2 uv) {
      float lighting=clamp(.48+.52*dot(normalize(carvingNormal),normalize(vec3(-.48,.77,.42))),0.,1.);
      vec2 finishRG=texture2D(finish,uv).rg;
      float grain=.60+.80*finishRG.r;
      float detail=mix(1.,clamp(grain,.66,1.12),.71);
      if(coatSite.x>.5 && coatSite.x<1.5 && coatSite.y<.55) {
        detail=mix(detail,clamp(grain,.13,1.),1.-smoothstep(.28,.65,grain));
      }
      float neck=coatSite.x<.5 ? smoothstep(.36,.61,anatomy.z) : 0.;
      float relief=detail*mix(.82,1.10,lighting)*(1.-.12*neck)*(1.+furShade);
      vec3 paintRGB=texture2D(paint,uv).rgb;
      float keepEye=catEyeColor(vec3(1.),paintRGB,uv,paintedFront).r-
                    catEyeColor(vec3(0.),paintRGB,uv,paintedFront).r;
      vec2 leather=texture2D(leatherMap,uv).rg;
      float keepLeather=coatSite.x>.5 && coatSite.x<1.5 ? max(leather.x,leather.y) : 0.;
      float coverage=(1.-finishRG.g)*(1.-keepLeather)*keepEye;
      return vec3(relief/1.5,coverage,lighting);
    }
    vec3 portraitCoatData() {
      if(portraitCoatField<.5) return vec3((anatomy.x+1.45)/2.9,anatomy.y,(anatomy.z+1.70)/2.75);
      if(portraitCoatField<1.5) return vec3((surfacePoint.x+1.65)/3.30,
        (surfacePoint.y+.16)/1.32,(surfacePoint.z+1.85)/3.75);
      if(portraitCoatField<2.5) return vec3(coatSite.x/3.,coatSite.y,(coatSite.z+.35)/1.80);
      return mix(portraitFinish(paintedFront),portraitFinish(paintedSide),smoothstep(.15,.85,paintMix));
    }
"""


def combine_surface_samples(planes, width, height):
    """Keep all projected coordinates on one actual surface at mesh seams."""
    scale = BAKER["SCALE"]
    fields = [plane.load() for plane in planes]
    region = fields[2]
    tiles = [Image.new("RGBA", (width, height)) for _ in planes]
    pixels = [tile.load() for tile in tiles]
    for y in range(height):
        for x in range(width):
            samples = [[], [], [], []]
            for yy in range(y * scale, (y + 1) * scale):
                for xx in range(x * scale, (x + 1) * scale):
                    encoded_part, _, _, alpha = region[xx, yy]
                    if alpha:
                        samples[round(encoded_part / 85)].append((xx, yy, alpha))
            coverage = [sum(alpha for _, _, alpha in surface) for surface in samples]
            part = max(range(4), key=coverage.__getitem__)
            weight = coverage[part]
            if not weight:
                continue
            alpha = round(sum(coverage) / (scale * scale))
            for index, (source, output) in enumerate(zip(fields, pixels)):
                rgb = [0, 0, 0]
                for xx, yy, sample_alpha in samples[part]:
                    r, g, b, _ = source[xx, yy]
                    rgb[0] += r * sample_alpha
                    rgb[1] += g * sample_alpha
                    rgb[2] += b * sample_alpha
                ready = tuple(round(value / weight) for value in rgb)
                if index == 2:
                    ready = (part * 85, *ready[1:])
                output[x, y] = (*ready, alpha)
    return tiles


def capture(output=BAKER["TEXTURES"]):
    viewer, model = BAKER["inject_calibration"]()
    model = model.replace("window.CatPigment.glsl+window.CatBrows.glsl+coatShader)",
                          "window.CatPigment.glsl+window.CatBrows.glsl+coatShader+`" + FIELDS + "`)")
    start = model.index("          vec3 srgb=clamp((1.+furShade)*mix(coatAt(")
    end = model.index(";`);", start)
    model = model[:start] + """          vec3 srgb=clamp(portraitCoatData(),0.,1.);
          diffuseColor.rgb=mix(srgb/12.92,pow((srgb+.055)/1.055,vec3(2.4)),step(vec3(.04045),srgb))""" + model[end:]
    marker = "      window.CatBrows.bind(T,shader,genes);"
    model = model.replace(marker, marker + "\n      shader.uniforms.portraitCoatField={value:window.coatField};")

    with sync_playwright() as playwright:
        chrome = environ.get("CAT_PORTRAIT_CHROME", r"C:\Program Files\Google\Chrome\Application\chrome.exe")
        browser = playwright.chromium.launch(executable_path=chrome, args=[
            "--use-angle=swiftshader", "--enable-unsafe-swiftshader"
        ])
        page = browser.new_page(viewport={"width": 640, "height": 480}, device_scale_factor=1)
        page.route("**/app.js", lambda req: req.fulfill(body="", content_type="text/javascript"))
        page.route("**/model.js", lambda req: req.fulfill(body=model, content_type="text/javascript"))
        page.route("**/viewer.js", lambda req: req.fulfill(body=viewer, content_type="text/javascript"))
        page.goto((BAKER["LAB"] / "index.html").as_uri())
        page.evaluate("""async () => {
            await CatModel.prepare(THREE);
            const main=document.createElement('canvas'), icon=document.createElement('canvas');
            main.style.cssText='width:32px;height:32px;display:block';
            icon.style.display='block';
            document.body.replaceChildren(main,icon);
            window.portrait={icon,viewer:createCatViewer({canvas:main,iconCanvas:icon,inventoryOnly:true})};
        }""")
        for age, height in (("adult", 4 * BAKER["CELL"]), ("kitten", BAKER["CELL"])):
            width = BAKER["WIDTH"]
            sheet = Image.new("RGBA", (4 * width, 2 * len(BAKER["EAR_SHAPES"]) * height))
            for bank, ears in enumerate(BAKER["EAR_SHAPES"]):
                for row, fur in enumerate(("short", "long")):
                    planes = []
                    for field in range(4):
                        page.evaluate("field => window.coatField = field", field)
                        planes.append(BAKER["draw"](page, age, height, fur, 1, ears, BAKER["probes"]()[0],
                                                   supersampled=True))
                    for field, tile in enumerate(combine_surface_samples(planes, width, height)):
                        assert tile.getchannel("A").getextrema() == (0, 255), (age, ears, fur, field)
                        sheet.paste(tile, (field * width, (bank * 2 + row) * height))
                print(age, ears, flush=True)
            sheet.save(output / f"cattery_coat_fields_{age}.png", optimize=True)
        browser.close()


if __name__ == "__main__":
    capture()
