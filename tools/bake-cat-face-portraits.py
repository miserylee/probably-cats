"""Project the cat model's registered brow and leather coordinates to inventory sprites."""
from base64 import b64decode
from io import BytesIO
from os import environ
from pathlib import Path
from runpy import run_path

from PIL import Image
from playwright.sync_api import sync_playwright

BAKE = run_path(str(Path(__file__).with_name("bake-cat-portraits.py")))
LAB, TEXTURES = BAKE["LAB"], BAKE["TEXTURES"]
CELL, WIDTH, SCALE = BAKE["CELL"], BAKE["WIDTH"], BAKE["SCALE"]
EAR_SHAPES = BAKE["EAR_SHAPES"]
FUR_TYPES = ("short", "long", "hairless")

FACE_MAPS = """
    uniform int portraitFacePass;
    vec3 faceLeather(vec2 uv) {
      if (coatSite.x < .5 || coatSite.x > 1.5) return vec3(0.);
      vec2 cover = texture2D(leatherMap, uv).rg;
      float value = dot(texture2D(paint, uv).rgb, vec3(.30,.59,.11));
      float relief = smoothstep(.075,.86,value) * max(cover.x,cover.y);
      return vec3(cover,relief);
    }
    vec3 faceBrow(vec2 uv) {
      vec2 brow = catBrowRegion(uv,kitten,coatSite.x).xy;
      if (brow.x < 0. || brow.x > 1.) return vec3(0.);
      return vec3(brow.x,clamp(.5+brow.y/100.,0.,1.),1.);
    }
    vec3 faceNose() {
      vec2 sheet = mix(vec2(1536.,1024.),vec2(2172.,724.),kitten);
      vec2 p = (vec2(paintedFront.x,1.-paintedFront.y)*sheet-
        mix(vec2(356.,293.),vec2(394.,408.),kitten))/mix(vec2(22.,14.),vec2(31.,18.),kitten);
      return vec3(clamp(.5+p/4.,0.,1.),0.);
    }
    vec3 portraitFaceMap() {
      float blend = smoothstep(.15,.85,paintMix);
      if (portraitFacePass == 0) return mix(faceLeather(paintedFront),faceLeather(paintedSide),blend);
      if (portraitFacePass == 1) return mix(faceBrow(paintedFront),faceBrow(paintedSide),blend);
      return faceNose();
    }
"""


def shaders():
    viewer, _ = BAKE["inject_calibration"]()
    model = (LAB / "model.js").read_text(encoding="utf-8")
    model = model.replace("window.CatBrows.glsl+coatShader)",
                          "window.CatBrows.glsl+coatShader+`" + FACE_MAPS + "`)")
    start = model.index("          vec3 srgb=clamp(")
    end = model.index(";`);", start)
    model = model[:start] + """          vec3 data = portraitFaceMap();
          diffuseColor.rgb=mix(data/12.92,pow((data+.055)/1.055,vec3(2.4)),step(vec3(.04045),data))""" + model[end:]
    bind = "      window.CatBrows.bind(T,shader,genes);"
    model = model.replace(bind, bind + "\n      shader.uniforms.portraitFacePass = {value:window.portraitFacePass};")
    return viewer, model


def render(page, age, height, fur, ear, pass_index):
    data = page.evaluate("""async ({age,width,height,fur,ear,passIndex}) => {
        const {icon, viewer} = window.portrait;
        icon.style.width = (width+2)+'px'; icon.style.height = (height+2)+'px';
        window.portraitFacePass = passIndex;
        viewer.update({...CatModel.defaults, age, furLength:fur, earShape:ear, coatPattern:1,
            browType:'fine'});
        for (let i=0; i<2; i++) await new Promise(requestAnimationFrame);
        return icon.toDataURL();
    }""", dict(age=age,width=WIDTH*SCALE,height=height*SCALE,fur=fur,ear=ear,passIndex=pass_index))
    with Image.open(BytesIO(b64decode(data.split(",", 1)[1]))) as image:
        assert image.size == (WIDTH*SCALE+2, height*SCALE+2), image.size
        return image.crop((1,1,WIDTH*SCALE+1,height*SCALE+1)).resize((WIDTH,height),Image.Resampling.BOX)


def bake(output=TEXTURES):
    viewer, model = shaders()
    with sync_playwright() as playwright:
        chrome = environ.get("CAT_PORTRAIT_CHROME", r"C:\Program Files\Google\Chrome\Application\chrome.exe")
        browser = playwright.chromium.launch(executable_path=chrome, args=[
            "--use-angle=swiftshader", "--enable-unsafe-swiftshader"
        ])
        page = browser.new_page(viewport={"width":640,"height":480}, device_scale_factor=1)
        page.route("**/app.js", lambda request: request.fulfill(body="", content_type="text/javascript"))
        page.route("**/model.js", lambda request: request.fulfill(body=model, content_type="text/javascript"))
        page.route("**/viewer.js", lambda request: request.fulfill(body=viewer, content_type="text/javascript"))
        page.goto((LAB / "index.html").as_uri())
        page.evaluate("""async () => {
            await CatModel.prepare(THREE);
            const main = document.createElement('canvas'), icon = document.createElement('canvas');
            main.style.cssText='width:32px;height:32px;display:block'; icon.style.display='block';
            document.body.replaceChildren(main, icon);
            window.portrait={icon,viewer:createCatViewer({canvas:main,iconCanvas:icon,inventoryOnly:true})};
        }""")
        for age, height in (("adult", 4*CELL), ("kitten", CELL)):
            sheet = Image.new("RGB", (len(EAR_SHAPES)*3*WIDTH, len(FUR_TYPES)*height))
            for row, fur in enumerate(FUR_TYPES):
                for ear_index, ear in enumerate(EAR_SHAPES):
                    for pass_index in range(3):
                        tile = render(page, age, height, fur, ear, pass_index)
                        if pass_index == 0:
                            assert all(tile.getchannel(channel).getextrema()[1] > 40 for channel in "RGB"), (age,fur,ear)
                        elif pass_index == 1:
                            assert tile.getchannel("B").getextrema()[1] > 100, (age,fur,ear)
                        sheet.paste(tile.convert("RGB"), ((pass_index*len(EAR_SHAPES)+ear_index)*WIDTH,row*height))
                print(age, fur, flush=True)
            sheet.save(output / f"cattery_portrait_{age}_face.png", optimize=True)
        browser.close()


if __name__ == "__main__":
    bake()
