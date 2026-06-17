from pathlib import Path

from PIL import Image, ImageFilter


PROJECT_DIR = Path(__file__).resolve().parents[1]
ASSET_DIR = PROJECT_DIR / "assets"
GENERATED_SOURCE = Path(
    r"C:\Users\Admin\.codex\generated_images\019ed4c7-b746-7472-a3bb-b0b4c5241bd8"
    r"\ig_09e01de3c4007b56016a32726a4b688191a4f07abd970b95a7.png"
)
ORIGINAL_SOURCE = Path(r"C:\Users\Admin\Desktop\logo.png")


def remove_chroma_key(image):
    image = image.convert("RGBA")
    pixels = image.load()
    width, height = image.size
    alpha = Image.new("L", (width, height), 255)
    alpha_pixels = alpha.load()

    for y in range(height):
        for x in range(width):
            r, g, b, _ = pixels[x, y]
            if g > 145 and r < 90 and b < 105 and g - r > 60 and g - b > 60:
                alpha_pixels[x, y] = 0
            elif g > 115 and r < 130 and b < 135 and g - r > 35 and g - b > 35:
                alpha_pixels[x, y] = 45

    image.putalpha(alpha.filter(ImageFilter.GaussianBlur(0.6)))
    return image.crop(image.getbbox())


def main():
    ASSET_DIR.mkdir(parents=True, exist_ok=True)
    icon = remove_chroma_key(Image.open(GENERATED_SOURCE))

    canvas = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    icon.thumbnail((224, 224), Image.Resampling.LANCZOS)
    canvas.alpha_composite(icon, ((256 - icon.width) // 2, (256 - icon.height) // 2))

    canvas.save(ASSET_DIR / "app-icon.png")
    canvas.save(
        ASSET_DIR / "app-icon.ico",
        sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)],
    )
    Image.open(ORIGINAL_SOURCE).save(ASSET_DIR / "source-logo.png")


if __name__ == "__main__":
    main()
