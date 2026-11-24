"""Bake the approved procedural cat model's portrait material responses for Unity.

Run from any directory with Python, Pillow, Playwright and Chromium available.
No browser or Python installation is required to play the built mod.
"""
from io import BytesIO
from pathlib import Path
from base64 import b64decode
from os import environ

from PIL import Image
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parent.parent
LAB = ROOT / "tools/cat-lab"
TEXTURES = ROOT / "src/DeepSpaceCattery/Textures/Items"
CELL, SCALE = 48, 4
WIDTH = 2 * CELL
NEUTRAL, STEP, COEFFICIENT_SCALE = 120, 68, 160
PATTERNS = tuple((fur, pattern) for fur in ("short", "long") for pattern in range(1, 6))
PRESETS = (*PATTERNS, ("hairless", 1))
EAR_SHAPES = ("upright", "slight", "folded")
COLS = 4
ROWS_PER_EAR_SHAPE = (len(PRESETS) + COLS - 1) // COLS


def inject_calibration():
    viewer = (LAB / "viewer.js").read_text(encoding="utf-8")
    grid_start = viewer.index("      iconContext.fillStyle")
    grid_end = viewer.index("      // Fixed-size cells", grid_start)
    viewer = (viewer[:grid_start] + viewer[grid_end:]).replace(
        "const nativeCell = 64;", f"const nativeCell = {CELL * SCALE};"
    )
    model = (LAB / "model.js").read_text(encoding="utf-8")
    bind_end = "      window.CatBrows.bind(T,shader,genes);"
    assert model.count(bind_end) == 1
    model = model.replace(bind_end, bind_end + """
      for (const [name, channels] of Object.entries(window.portraitCalibration)) {
        shader.uniforms[name].value.set(...channels);
      }""")
    return viewer, model


def probes():
    neutral = tuple([NEUTRAL / 255] * 3)
    high = (NEUTRAL + STEP) / 255

    def channel(index):
        return tuple(high if i == index else neutral[i] for i in range(3))

    base = dict(baseRGB=neutral, patchRGB=neutral, skinRGB=neutral,
                irisLeft=(*neutral, 1), irisRight=(*neutral, 1))
    return (base,
            {**base, "baseRGB": channel(0), "skinRGB": channel(0), "patchRGB": channel(1)},
            {**base, "irisLeft":(*channel(2), 1)},
            {**base, "irisRight":(*channel(2), 1)})


def draw(page, age, height, fur, pattern, ear_shape, calibration, *, supersampled=False, white=.46, bands=.55):
    data = page.evaluate("""async ({age, width, height, calibrate, fur, pattern, earShape, white, bands}) => {
        const {icon, viewer} = window.portrait;
        icon.style.width = (width + 2) + 'px';
        icon.style.height = (height + 2) + 'px';
        window.portraitCalibration = calibrate;
        viewer.update({...CatModel.defaults, age, furLength: fur, coatPattern: pattern, earShape,
            bands, white, baseColor: 'ginger', patchColor: 'charcoal',
            eyeMode: 'odd', eyeLeft: 'amber', eyeRight: 'blue', browType: 'none'});
        for (let i = 0; i < 2; i++) await new Promise(requestAnimationFrame);
        return icon.toDataURL();
    }""", dict(age=age, width=WIDTH * SCALE, height=height * SCALE,
                  calibrate=calibration, fur=fur, pattern=pattern, earShape=ear_shape, white=white, bands=bands))
    with Image.open(BytesIO(b64decode(data.split(",", 1)[1]))) as screenshot:
        assert screenshot.size == (WIDTH * SCALE + 2, height * SCALE + 2), screenshot.size
        tile = screenshot.crop((1, 1, 1 + WIDTH * SCALE, 1 + height * SCALE))
        return tile if supersampled else tile.resize((WIDTH, height), Image.Resampling.BOX)


def material_mask(neutral, fur_probe, left_probe, right_probe):
    scale = COEFFICIENT_SCALE / STEP

    def weight(delta):
        return max(0, min(255, round(delta * scale)))

    mask = Image.new("RGBA", neutral.size)
    mask.putdata([
        (weight(f[0] - n[0]), weight(f[1] - n[1]), weight(l[2] - n[2]), weight(r[2] - n[2]))
        if n[3] >= 8 else (0, 0, 0, 0)
        for n, f, l, r in zip(neutral.getdata(), fur_probe.getdata(), left_probe.getdata(), right_probe.getdata())
    ])
    return mask


def bake(output=TEXTURES, *, calico_only=False):
    viewer, model = inject_calibration()
    with sync_playwright() as playwright:
        chrome = environ.get("CAT_PORTRAIT_CHROME", r"C:\Program Files\Google\Chrome\Application\chrome.exe")
        browser = playwright.chromium.launch(executable_path=chrome, args=[
            "--use-angle=swiftshader", "--enable-unsafe-swiftshader"
        ])
        page = browser.new_page(viewport={"width": 640, "height": 480}, device_scale_factor=1)
        page.route("**/app.js", lambda req: req.fulfill(body="", content_type="text/javascript"))
        page.route("**/model.js", lambda req: req.fulfill(body=model, content_type="text/javascript"))
        page.route("**/viewer.js", lambda req: req.fulfill(body=viewer, content_type="text/javascript"))
        page.goto((LAB / "index.html").as_uri())
        page.evaluate("""async () => {
            await CatModel.prepare(THREE);
            const main = document.createElement('canvas'), icon = document.createElement('canvas');
            main.style.cssText = 'width:32px;height:32px;display:block';
            icon.style.display = 'block';
            document.body.replaceChildren(main, icon);
            window.portrait = {icon, viewer:createCatViewer({canvas:main, iconCanvas:icon, inventoryOnly:true})};
        }""")
        for age, height in (("adult", 4 * CELL), ("kitten", CELL)):
            # Project the original painted model in the same pose and ear/fur geometry as the runtime.
            paint = Image.new("RGBA", (3 * WIDTH, 6 * height))
            for bank, ears in enumerate(EAR_SHAPES):
                for row, fur in enumerate(("short", "long")):
                    for column, white in enumerate((.20, .55, .90)):
                        tile = draw(page, age, height, fur, 0, ears, probes()[0], white=white, bands=.25)
                        paint.paste(tile, (column * WIDTH, (bank * 2 + row) * height))
            paint.save(output / f"cattery_calico_{age}.png", optimize=True)
            if calico_only:
                continue
            reference_sheet = Image.new("RGBA", (COLS * WIDTH, len(EAR_SHAPES) * ROWS_PER_EAR_SHAPE * height))
            mask_sheet = Image.new("RGBA", reference_sheet.size)
            for bank, ear_shape in enumerate(EAR_SHAPES):
                for index, (fur, pattern) in enumerate(PRESETS):
                    neutral, fur_probe, left_probe, right_probe = (
                        draw(page, age, height, fur, pattern, ear_shape, probe) for probe in probes()
                    )
                    mask = material_mask(neutral, fur_probe, left_probe, right_probe)
                    preset = (age, ear_shape, fur, pattern)
                    assert all(channel.getextrema()[1] > 12 for channel in (mask.getchannel("R"),
                               mask.getchannel("B"), mask.getchannel("A"))), preset
                    assert neutral.getchannel("A").getextrema() == (0, 255), preset
                    row = bank * ROWS_PER_EAR_SHAPE + index // COLS
                    origin = ((index % COLS) * WIDTH, row * height)
                    reference_sheet.paste(neutral, origin)
                    mask_sheet.paste(mask, origin)
                print(age, ear_shape, flush=True)
            reference_sheet.save(output / f"cattery_portrait_{age}_reference.png", optimize=True)
            mask_sheet.save(output / f"cattery_portrait_{age}_materials.png", optimize=True)
        browser.close()


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--calico-only", action="store_true")
    bake(calico_only=parser.parse_args().calico_only)

