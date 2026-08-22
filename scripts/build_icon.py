"""
Nougat App-Icon-Generator.

Design: Bonbon-/Praline-Silhouette in Kroste-Gold (#E0B14C) auf
abgerundetem dunklem Grund (#161C23). Thematisch: eingewickeltes
NuGet-Paket. Text- und schriftfrei, funktioniert auch als 16x16-Favicon.

Erzeugt:
- Nougat/Assets/nougat.png   (256x256, master)
- Nougat/Assets/nougat.ico   (Windows-Multi-Res)
"""

from pathlib import Path

from PIL import Image, ImageDraw

GOLD     = (224, 177, 76, 255)     # #E0B14C
GOLD_D   = (176, 138, 55, 255)     # dunkler für Schatten
SURFACE  = (22, 28, 35, 255)       # #161C23
BORDER   = (42, 51, 61, 255)       # #2A333D
TRANSP   = (0, 0, 0, 0)

SIZE = 256
CORNER = 48

def make_icon(size: int) -> Image.Image:
    """Baut das Icon in der angegebenen Kantenlaenge."""
    scale = size / 256
    img = Image.new("RGBA", (size, size), TRANSP)
    d = ImageDraw.Draw(img)

    corner = int(CORNER * scale)
    d.rounded_rectangle([(0, 0), (size - 1, size - 1)],
                        radius=corner, fill=SURFACE, outline=BORDER,
                        width=max(1, int(2 * scale)))

    cx = size / 2
    cy = size / 2
    body_w = int(120 * scale)
    body_h = int(80 * scale)
    body_r = int(20 * scale)

    # Bonbon-Koerper (abgerundetes Rechteck in der Mitte)
    d.rounded_rectangle(
        [(cx - body_w / 2, cy - body_h / 2),
         (cx + body_w / 2, cy + body_h / 2)],
        radius=body_r, fill=GOLD, outline=GOLD_D,
        width=max(2, int(3 * scale))
    )

    # Linker Zipfel (Dreieck)
    tip_w = int(52 * scale)
    tip_h = int(64 * scale)
    left = [
        (cx - body_w / 2, cy),
        (cx - body_w / 2 - tip_w, cy - tip_h / 2),
        (cx - body_w / 2 - tip_w, cy + tip_h / 2),
    ]
    d.polygon(left, fill=GOLD, outline=GOLD_D)

    # Rechter Zipfel (Dreieck)
    right = [
        (cx + body_w / 2, cy),
        (cx + body_w / 2 + tip_w, cy - tip_h / 2),
        (cx + body_w / 2 + tip_w, cy + tip_h / 2),
    ]
    d.polygon(right, fill=GOLD, outline=GOLD_D)

    # Detail: zwei schmale Baender im Koerper (angedeutete Verpackungsfalten)
    band = max(1, int(2 * scale))
    d.line([(cx - body_w / 2 + int(20 * scale), cy - body_h / 2 + int(12 * scale)),
            (cx - body_w / 2 + int(20 * scale), cy + body_h / 2 - int(12 * scale))],
           fill=GOLD_D, width=band)
    d.line([(cx + body_w / 2 - int(20 * scale), cy - body_h / 2 + int(12 * scale)),
            (cx + body_w / 2 - int(20 * scale), cy + body_h / 2 - int(12 * scale))],
           fill=GOLD_D, width=band)

    return img

if __name__ == "__main__":
    # Repo-relativ: <repo>/scripts/build_icon.py -> <repo>/Nougat/Assets.
    # Absolute /home/<user>/...-Pfade machen das Skript auf dem Arbeitslaptop
    # unbrauchbar (in DTM genau so passiert).
    assets = Path(__file__).resolve().parent.parent / "Nougat" / "Assets"
    assets.mkdir(parents=True, exist_ok=True)

    master = make_icon(256)
    master.save(assets / "nougat.png", "PNG")
    print(f"Wrote {assets / 'nougat.png'} (256x256)")

    sizes = [16, 24, 32, 48, 64, 128, 256]
    icons = [make_icon(s) for s in sizes]
    icons[0].save(
        assets / "nougat.ico",
        format="ICO",
        sizes=[(s, s) for s in sizes],
        append_images=icons[1:],
    )
    print(f"Wrote {assets / 'nougat.ico'} (multi-res: {sizes})")
