import sys; sys.argv = ['preview.py']
import preview as P
import numpy as np
from PIL import Image, ImageDraw
views = [
    (0.265, (6, 69, 34), (-10, 66, -30), '日出 sunrise'),
    (0.36, (6, 69, 34), (-2, 66, 0), '上午 morning'),
    (0.5, (40, 70, -10), (0, 66, -20), '正午 noon'),
    (0.64, (6, 69, 34), (-2, 66, 0), '下午 afternoon'),
    (0.735, (6, 69, 34), (-2, 66, 0), '日落 sunset'),
    (0.02, (-10, 68, 30), (-24, 66, 8), '夜晚 night (torch-lit house)'),
    (0.45, (0, 67.5, 14), (0, 64, 0), '水面 water'),
    (0.70, (4, 67, 12), (-6, 65, -2), '水面傍晚 water, evening'),
]
imgs = []
for tod, pos, tgt, name in views:
    img = P.render(tod, pos, tgt, out=f'day_{tod:.3f}.png', dump=True)[..., :3].copy()
    imgs.append((img, name))
tiles = []
for img, name in imgs:
    im = Image.fromarray(img); d = ImageDraw.Draw(im)
    d.rectangle((0, 0, 8 + 7 * len(name.split(' ', 1)[1]), 16), fill=(0, 0, 0))
    d.text((4, 2), name.split(' ', 1)[1], fill=(255, 255, 255))
    tiles.append(np.array(im))
rows = [np.concatenate(tiles[i:i+2], 1) for i in range(0, len(tiles), 2)]
Image.fromarray(np.concatenate(rows, 0)).save('daycycle.png')
