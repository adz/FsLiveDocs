#!/usr/bin/env python3
"""Generate the FsLiveDocs logo assets: one static PNG and one animated WebP
per daisyUI theme, written to docs/content/.

The animation reproduces the CLI banner in `LiveDocsBanner.fs` — the same
"Fs Live Docs" figlet wordmark, block colors, and reveal timing — rendered
directly to pixels (no terminal recording, no palette quantization, no
Spectre live-redraw artifacts).

Keep WORDMARK, WIDTH, the block colors, and the timing in sync with
`src/FsLiveDocs.Cli/LiveDocsBanner.fs`.
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

# --- Ported from src/FsLiveDocs.Cli/LiveDocsBanner.fs ---
WIDTH = 74
WORDMARK = [
    "  _____           _       _                    ____                       ",
    " |  ___|  ___    | |     (_) __   __   ___    |  _ \\    ___     ___   ___ ",
    " | |_    / __|   | |     | | \\ \\ / /  / _ \\   | | | |  / _ \\   / __| / __|",
    " |  _|   \\__ \\   | |___  | |  \\ V /  |  __/   | |_| | | (_) | | (__  \\__ \\",
    " |_|     |___/   |_____| |_|   \\_/    \\___|   |____/   \\___/   \\___| |___/",
]
HEIGHT = len(WORDMARK)
RUN_SEED = 0x2F6E2B1  # fixed seed for deterministic wall noise
BLOCKS = ["\u2588", "\u2593", "\u2592", "\u2591"]  # full, dark, medium, light

# Per-theme variants. Light themes use dark-blue lettering, dark themes light-blue.
# Backgrounds are the daisyUI 4 `--b1` (base-100) colors for each theme.
LIGHT_BLOCKS = ["#3fa7d6", "#2e86ab", "#1b6ca8", "#144361"]
DARK_BLOCKS = ["#72d7ff", "#5aa9c8", "#477c91", "#3b5965"]
VARIANTS = {
    "light":     {"bg": "#ffffff", "letter": "#1b6ca8", "blocks": LIGHT_BLOCKS},
    "dark":      {"bg": "#1d232a", "letter": "#46b4eb", "blocks": DARK_BLOCKS},
    "cupcake":   {"bg": "#faf7f5", "letter": "#1b6ca8", "blocks": LIGHT_BLOCKS},
    "dracula":   {"bg": "#282a36", "letter": "#46b4eb", "blocks": DARK_BLOCKS},
    "retro":     {"bg": "#ece3ca", "letter": "#1b6ca8", "blocks": LIGHT_BLOCKS},
    "cyberpunk": {"bg": "#ffee00", "letter": "#1b6ca8", "blocks": LIGHT_BLOCKS},
}

FONT_SIZE = 20
CELL_H = 22        # line height tuned for a terminal-like cell aspect
BASELINE = 17      # baseline offset within a cell (anchor "ls")
PAD = 12           # symmetric padding added after cropping to the wordmark
OUT_HEIGHT = 96    # 2x the 48px header logo height (retina)
FRAME_MS = 40      # animation frame interval (25 fps)
ANIM_START_MS = 440    # start once the wall is fully built (no empty lead-in)
ANIM_END_MS = 2000     # end once the reveal has settled
STATIC_MS = 1600       # settled, fully revealed frame


def shade(row, col, frame):
    v = RUN_SEED ^ (row * 73856093) ^ (col * 19349663) ^ (frame * 83492791)
    v = ((v ^ (v >> 16)) * 0x7FEB352D) & 0xFFFFFFFF
    v = ((v ^ (v >> 15)) * 0x846CA68B) & 0xFFFFFFFF
    return ((v ^ (v >> 16)) & 0xFFFFFFFF) % 100


def fade_for(sample):
    return 0 if sample < 72 else 1 if sample < 87 else 2 if sample < 96 else 3


def canvas(elapsed_ms):
    """Return the 5x74 character grid and style grid for a point in the animation."""
    row_interval = 90.0
    scroll = HEIGHT * row_interval
    visible = min(HEIGHT, 1 + int(elapsed_ms / row_interval))
    effect = max(0.0, elapsed_ms - scroll)
    chars = [[" "] * WIDTH for _ in range(HEIGHT)]
    styles = [[0] * WIDTH for _ in range(HEIGHT)]  # 0=plain, 1=letter, 2..5=block shade+2

    if elapsed_ms < scroll:
        frame = int(elapsed_ms / row_interval)
        for row in range(visible):
            for col in range(WIDTH):
                b = fade_for(shade(row, col, frame))
                chars[row][col] = BLOCKS[b]
                styles[row][col] = 2 + b
    else:
        for row in range(HEIGHT):
            for col in range(min(WIDTH, len(WORDMARK[row]))):
                glyph = WORDMARK[row][col]
                if glyph != " ":
                    chars[row][col] = glyph
                    styles[row][col] = 1
        final_frame = HEIGHT - 1
        for row in range(HEIGHT):
            for col in range(WIDTH):
                b = fade_for(shade(row, col, final_frame))
                launch = 80.0 + (HEIGHT - 1 - row) * 155.0 + ((col * 17) % 9) * 10.0
                age = effect - launch
                if age < 0.0:
                    chars[row][col] = BLOCKS[b]
                    styles[row][col] = 2 + b
                elif age < 620.0:
                    rise = 1 + int(age / 125.0)
                    dest = row - rise
                    if dest >= 0:
                        bb = min(3, b + int(age / 190.0))
                        chars[dest][col] = BLOCKS[bb]
                        styles[dest][col] = 2 + bb
    return chars, styles


def find_font():
    candidates = [
        "/usr/share/fonts/google-noto-vf/NotoSansMono[wght].ttf",
        "/usr/share/fonts/dejavu/DejaVuSansMono.ttf",
        "/usr/share/fonts/adwaita-mono-fonts/AdwaitaMono-Regular.ttf",
        "/usr/share/fonts/urw-base35/NimbusMonoPS-Regular.otf",
    ]
    for path in candidates:
        if Path(path).exists():
            return path
    raise SystemExit("No monospace font found; install one and retry.")


def hex2rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4))


def render_frame(elapsed_ms, variant, font, cell_w):
    var = VARIANTS[variant]
    chars, styles = canvas(elapsed_ms)
    block_colors = [hex2rgb(c) for c in var["blocks"]]
    bg = hex2rgb(var["bg"])
    letter = hex2rgb(var["letter"])
    img = Image.new("RGB", (WIDTH * cell_w + 2 * PAD, HEIGHT * CELL_H + 2 * PAD), bg)
    draw = ImageDraw.Draw(img)
    for row in range(HEIGHT):
        for col in range(WIDTH):
            st = styles[row][col]
            if st == 0:
                continue
            color = letter if st == 1 else block_colors[st - 2]
            draw.text(
                (PAD + col * cell_w, PAD + row * CELL_H + BASELINE),
                chars[row][col], font=font, fill=color, anchor="ls",
            )
    return img


def content_bbox(img, bg):
    """Bounding box of every pixel that differs from the background."""
    xs, ys = [], []
    for y in range(img.size[1]):
        for x in range(img.size[0]):
            if img.getpixel((x, y)) != bg:
                xs.append(x)
                ys.append(y)
    if not xs:
        return (0, 0, img.size[0], img.size[1])
    return (min(xs), min(ys), max(xs) + 1, max(ys) + 1)


def crop_and_pad(img, box, pad, bg):
    out = Image.new("RGB", (box[2] - box[0] + 2 * pad, box[3] - box[1] + 2 * pad), bg)
    out.paste(img.crop(box), (pad, pad))
    return out


def scale_to_height(img, height):
    width = round(img.size[0] * height / img.size[1])
    return img.resize((width, height), Image.LANCZOS)


def main():
    repo_root = Path(__file__).resolve().parent.parent
    out_dir = repo_root / "docs" / "content"
    out_dir.mkdir(parents=True, exist_ok=True)

    font = ImageFont.truetype(find_font(), FONT_SIZE)
    cell_w = round(font.getlength("M"))  # match the font's actual advance

    for variant, var in VARIANTS.items():
        bg = hex2rgb(var["bg"])
        static = render_frame(STATIC_MS, variant, font, cell_w)
        box = content_bbox(static, bg)

        png_path = out_dir / f"fslivedocs-logo-{variant}.png"
        webp_path = out_dir / f"fslivedocs-logo-{variant}.webp"

        scale_to_height(crop_and_pad(static, box, PAD, bg), OUT_HEIGHT).save(png_path)

        frames = [
            scale_to_height(crop_and_pad(render_frame(ms, variant, font, cell_w), box, PAD, bg), OUT_HEIGHT)
            for ms in range(ANIM_START_MS, ANIM_END_MS, FRAME_MS)
        ]
        frames[0].save(
            webp_path,
            save_all=True,
            append_images=frames[1:],
            duration=FRAME_MS,
            loop=1,
            quality=65,
        )
        rel = lambda p: p.relative_to(repo_root)
        print(f"wrote {rel(png_path)} and {rel(webp_path)}")


if __name__ == "__main__":
    main()
