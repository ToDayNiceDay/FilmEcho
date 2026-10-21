from PIL import Image, ImageDraw
from pathlib import Path
root=Path(__file__).resolve().parent.parent
(root/'assets').mkdir(exist_ok=True)
im=Image.new('RGBA',(256,256),(0,0,0,0)); d=ImageDraw.Draw(im)
d.rounded_rectangle((8,8,248,248),radius=56,fill='#1e675c')
d.rounded_rectangle((43,49,211,193),radius=16,fill='#f4f6f2')
d.polygon([(128,66),(128,177),(99,165),(60,165),(60,65),(98,65)],fill='#d9e5df')
d.line((128,67,128,178),fill='#1e675c',width=6)
d.polygon([(146,99),(146,154),(190,127)],fill='#cc9136')
d.line((72,87,107,87),fill='#1e675c',width=6)
d.line((72,108,107,108),fill='#1e675c',width=6)
d.line((72,129,100,129),fill='#1e675c',width=6)
im.save(root/'assets/FilmEcho.png')
im.save(root/'assets/FilmEcho.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
