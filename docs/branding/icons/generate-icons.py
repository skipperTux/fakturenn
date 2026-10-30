#!/usr/bin/env python3
"""Deterministically render favicons and app icons from the approved hybrid SVG icon masters.
Requires: python3 -m pip install Pillow
Run: python3 generate-icons.py
"""
from pathlib import Path
from PIL import Image
import base64
import io
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
SIZES = (16, 32, 48, 64, 96, 128, 180, 192, 256, 512)
for product in ('fakturenn', 'tailh'):
    directory = ROOT / product
    master_svg = directory / 'icon-master.svg'
    tree = ET.parse(master_svg)
    svg_image = tree.getroot().find('{http://www.w3.org/2000/svg}image')
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
        master.resize((size, size), Image.Resampling.LANCZOS).save(directory / filename)
    for size in (16, 32):
        (directory / f'favicon-{size}x{size}.png').write_bytes((directory / f'icon-{size}.png').read_bytes())
    (directory / 'favicon.svg').write_bytes(master_svg.read_bytes())
    # Pillow assembles the ICO internally; these are true embedded resolution variants.
    master.save(directory / 'favicon.ico', format='ICO', sizes=[(16, 16), (32, 32), (48, 48)])
    print(f'Generated {product} icons')
