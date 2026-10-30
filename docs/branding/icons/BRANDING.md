# Fakturenn / Tailh — favicon and app-icon assets

## Canonical branding

These icons are extracted **without generative image tools** from the previously approved hybrid SVG logos. The SVGs contain embedded raster artwork. This package therefore keeps **the same original pixels** in the icon master, rather than inventing new vector paths.

The artwork retains the approved lighthouse/document, **short / three long / short document lines**, handwritten signature, cliffs, waves, beam/check (Fakturenn), and attached tag (Tailh). No wordmark appears in either icon.

- `fakturenn/icon-master.svg` and `tailh/icon-master.svg`: canonical square icon masters, containing embedded PNG artwork, on white backgrounds.
- Each `icon-source.png`: the matching 768×768 master raster embedded in its SVG.
- Each `favicon.svg`: copy of the corresponding canonical square master.
- Shared positioning and whitespace are intentional for square app icons.

**Important:** these are *hybrid* SVG masters, not traced vector paths. They scale deterministically but do not have infinite raster detail. At 16×16 some original fine details cannot remain legible; no shapes or lines are intentionally removed during export. Changing the shapes for a small-size variant would require explicit design approval.

## Deterministic regeneration

With Python 3 and Pillow:

```bash
python3 -m pip install Pillow
python3 generate-icons.py
```

This script reads the approved icon-master SVGs directly. It does not regenerate or retouch any artwork. It writes PNGs via LANCZOS scaling and the ICO container using Pillow.

## Exported per product

- `favicon.svg`
- `favicon.ico` (16, 32, 48 px)
- `favicon-16x16.png`, `favicon-32x32.png`
- `icon-16.png`, `icon-32.png`, `icon-48.png`, `icon-64.png`, `icon-96.png`, `icon-128.png`, `icon-192.png`, `icon-256.png`, `icon-512.png`
- `apple-touch-icon.png` (180 px)

All outputs have white backgrounds. The source artwork stays consistent across sizes.

## Web integration

```html
<link rel="icon" type="image/svg+xml" href="/favicon.svg">
<link rel="icon" type="image/x-icon" href="/favicon.ico" sizes="any">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
```

When creating a PWA manifest, point icons at `/icon-192.png` and `/icon-512.png` with `purpose: "any"`. Do **not** declare these icons maskable: maskable artwork needs its own safe-area layout and validation.

## Original full-size wordmarks

The previously approved full-size Fakturenn and Tailh branding uses Inter Bold, 158 px, baseline y=625, identical typographic size and weight, in the original logo coordinate system. This package contains **icon-only** assets and does not modify those wordmarks.
