# Fakturenn branding

The logo, the app icons, and how to regenerate the icons. Who may use the name
and the logo, and how, is in [TRADEMARKS.md](../../TRADEMARKS.md).

## Logo

- `fakturenn-logo.svg` — master
- `fakturenn-logo.png` — raster export

The artwork is the approved lighthouse and document. Only the generated
wordmark area of the original image was removed and replaced with real text:

- Font: Inter, Bold (700)
- Size: 158 px
- Baseline: y = 625 px; wordmark start: x = 650 px
- Colour: `#001f40`

The SVG is a hybrid: the artwork is an embedded raster image, the wordmark is
SVG text. To edit the wordmark, install Inter. For a font-independent SVG,
convert the wordmark to paths in Inkscape.

## Icons

`icons/` holds the favicons and app icons. They are cut from the logo artwork
without generative image tools, so they keep the logo's own pixels: the
lighthouse and document, the short, three long and short document lines, the
handwritten signature, the cliffs, the waves and the beam with its check. They
carry no wordmark.

- `icon-master.svg` — canonical square master, a hybrid SVG embedding
  `icon-source.png` (768 × 768) on a white background.
- `favicon.svg` — a copy of `icon-master.svg`.
- `favicon.ico` (16, 32, 48 px), `favicon-16x16.png`, `favicon-32x32.png`
- `icon-16.png` to `icon-512.png`, and `apple-touch-icon.png` (180 px)

All outputs have white backgrounds. Being raster underneath, the icons scale
deterministically but carry no extra detail: at 16 × 16 some fine lines
cannot stay legible. A simplified small-size variant would change the shapes
and needs explicit design approval.

## Regenerating the icons

`generate-icons.py` reads `icon-master.svg`, scales the embedded master with
LANCZOS, and writes every file listed above into `icons/`. It never retouches
the artwork.

```bash
uv run --no-project --with Pillow python3 docs/branding/icons/generate-icons.py
```

## Web integration

```html
<link rel="icon" type="image/x-icon" href="/favicon.ico" sizes="any">
<link rel="icon" type="image/svg+xml" href="/favicon.svg">
<link rel="apple-touch-icon" href="/apple-touch-icon.png">
```

`favicon.svg` weighs about 320 KB, because it embeds the raster master, against
under 5 KB for `favicon.ico`. It buys no sharpness over the PNGs, so leave it
out unless a browser needs it.

For a PWA manifest, point the icons at `/icon-192.png` and `/icon-512.png` with
`purpose: "any"`. Do **not** declare them maskable: maskable artwork needs its
own safe-area layout.
