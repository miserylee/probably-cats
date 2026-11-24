"""Project the existing cat model's shape ranges into reusable signed displacement fields."""
from base64 import b64decode
from collections import deque
from os import environ
from pathlib import Path

from PIL import Image
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parent.parent
LAB = ROOT / "tools/cat-lab"
TEXTURES = ROOT / "src/DeepSpaceCattery/Textures/Items"
TRAITS = ("fullness", "cheeks", "muzzle", "eyes", "ears")
FURS = ("short", "long", "hairless")
EARS = ("upright", "slight", "folded")
WIDTH = 96


def pack_projection(raw, width, height):
    """Average covered samples and extend the flow beyond its neutral silhouette."""
    source_width = width * 2
    assert len(raw) == width * height * 16
    coordinates = []
    remaining = deque()
    for y in range(height):
        for x in range(width):
            samples = []
            for oy, ox in ((0, 0), (0, 1), (1, 0), (1, 1)):
                offset = ((2 * y + oy) * source_width + (2 * x + ox)) * 4
                sample = raw[offset:offset + 4]
                if any(sample):
                    samples.append(sample)
            if samples:
                remaining.append((x, y))
                coordinates.append(tuple(round(sum(channel) / len(samples)) for channel in zip(*samples)))
            else:
                coordinates.append(None)
    assert remaining, "Empty portrait displacement field"

    # A translated outline must also be found when its destination is in a formerly transparent texel.
    while remaining:
        x, y = remaining.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            index = ny * width + nx
            if 0 <= nx < width and 0 <= ny < height and coordinates[index] is None:
                coordinates[index] = coordinates[y * width + x]
                remaining.append((nx, ny))

    # Render targets and Unity use bottom-first rows; the PNG sheet stores them top-first.
    output = Image.new("RGBA", (width, height))
    output.putdata(coordinates)
    return output.transpose(Image.Transpose.FLIP_TOP_BOTTOM)


def bake(output=TEXTURES):
    viewer = (LAB / "viewer.js").read_text(encoding="utf-8")
    frame_source = viewer[viewer.index("  const framing = {"):viewer.index("  const stagePlaces = {")]
    capture_source = (LAB / "portrait-shape-bake.js").read_text(encoding="utf-8")
    with sync_playwright() as playwright:
        chrome = environ.get("CAT_PORTRAIT_CHROME", r"C:\Program Files\Google\Chrome\Application\chrome.exe")
        browser = playwright.chromium.launch(executable_path=chrome, args=[
            "--use-angle=swiftshader", "--enable-unsafe-swiftshader"
        ])
        page = browser.new_page()
        page.route("**/app.js", lambda req: req.fulfill(body="", content_type="text/javascript"))
        page.goto((LAB / "index.html").as_uri())
        page.add_script_tag(content=capture_source)
        page.evaluate("async () => { await CatModel.prepare(THREE); }")
        for age, height in (("adult", 192), ("kitten", 48)):
            page.evaluate("""([age, width, height]) => {
                %s
                window.shapeBaker = createPortraitShapeBaker(THREE, width, height, age, framing[age]);
            }""" % frame_source, [age, WIDTH, height])
            sheet = Image.new("RGBA", (WIDTH * len(TRAITS), height * len(FURS) * len(EARS)))
            for fur_index, fur in enumerate(FURS):
                for ear_index, ear in enumerate(EARS):
                    for trait_index, trait in enumerate(TRAITS):
                        data = page.evaluate("""([fur, ear, trait]) => shapeBaker.capture({
                            furLength: fur, earShape: ear
                        }, trait)""", [fur, ear, trait])
                        tile = pack_projection(b64decode(data), WIDTH, height)
                        row = fur_index * len(EARS) + ear_index
                        sheet.paste(tile, (trait_index * WIDTH, row * height))
                    print(age, fur, ear, flush=True)
            sheet.save(output / f"cattery_portrait_{age}_shape.png", optimize=True)
            page.evaluate("shapeBaker.dispose()")
        browser.close()


if __name__ == "__main__":
    bake()
