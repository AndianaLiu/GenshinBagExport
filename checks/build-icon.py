from pathlib import Path
from PIL import Image
import sys
root = Path(__file__).resolve().parents[1]
source = Path(sys.argv[1]) if len(sys.argv) > 1 else root / 'Assets/Keqing.png'
with Image.open(source) as image:
    clean = Image.frombytes('RGBA', image.size, image.convert('RGBA').tobytes())
clean.save(root / 'Assets/Keqing.png')
clean.save(root / 'Assets/Keqing.ico', format='ICO', sizes=[(n,n) for n in (16,24,32,48,64,128,256)], bitmap_format='bmp')
with Image.open(root / 'Assets/Keqing.ico') as icon:
    assert len(icon.ico.sizes()) == 7
print('Created 7-size Windows icon')
