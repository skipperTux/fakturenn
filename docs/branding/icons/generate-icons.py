#!/usr/bin/env python3
"""Deterministically render the favicons and app icons from the approved hybrid SVG icon master.

Run from anywhere: uv run --no-project --with Pillow python3 generate-icons.py
Writes into the directory this script lives in.
"""
from pathlib import Path
from PIL import Image
import base64
import io
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
SIZES = (16, 32, 48, 64, 96, 128, 180, 192, 256, 512)

master_svg = ROOT / 'icon-master.svg'
svg_image = ET.parse(master_svg).getroot().find('{http://www.w3.org/2000/svg}image')
if svg_image is None:
    raise ValueError(f'Missing embedded master artwork in {master_svg}')
data_uri = svg_image.attrib.get('href')
if not data_uri or not data_uri.startswith('data:image/png;base64,'):
    raise ValueError(f'Expected embedded PNG in {master_svg}')
master = Image.open(io.BytesIO(base64.b64decode(data_uri.split(',', 1)[1]))).convert('RGB')
if master.size != (768, 768):
    raise ValueError(f'Unexpected master size: {master.size}')

for size in SIZES:
    filename = 'apple-touch-icon.png' if size == 180 else f'icon-{size}.png'
    master.resize((size, size), Image.Resampling.LANCZOS).save(ROOT / filename)
for size in (16, 32):
    (ROOT / f'favicon-{size}x{size}.png').write_bytes((ROOT / f'icon-{size}.png').read_bytes())
(ROOT / 'favicon.svg').write_bytes(master_svg.read_bytes())
# Pillow assembles the ICO itself; these are true embedded resolution variants.
master.save(ROOT / 'favicon.ico', format='ICO', sizes=[(16, 16), (32, 32), (48, 48)])
print('Generated Fakturenn icons')
