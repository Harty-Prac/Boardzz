# Boardzz logo: blurple rounded square with white sound-wave bars.
from PIL import Image, ImageDraw
import sys

out = sys.argv[1]
size = 1024  # draw large, downscale for crisp edges
img = Image.new("RGBA", (size, size), (0, 0, 0, 0))

# vertical gradient #6C78FF -> #4752C4, clipped to a rounded square
grad = Image.new("RGBA", (size, size))
top, bottom = (0x6C, 0x78, 0xFF), (0x47, 0x52, 0xC4)
gd = ImageDraw.Draw(grad)
for y in range(size):
    t = y / (size - 1)
    gd.line([(0, y), (size, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,))
mask = Image.new("L", (size, size), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=230, fill=255)
img.paste(grad, (0, 0), mask)

# five rounded white bars, tallest in the middle
d = ImageDraw.Draw(img)
heights = [0.30, 0.52, 0.70, 0.52, 0.30]
barWidth, gap = 104, 52
total = len(heights) * barWidth + (len(heights) - 1) * gap
x = (size - total) // 2
for h in heights:
    barHeight = int(size * h)
    y0 = (size - barHeight) // 2
    d.rounded_rectangle([x, y0, x + barWidth, y0 + barHeight], radius=barWidth // 2, fill=(255, 255, 255, 255))
    x += barWidth + gap

img.resize((256, 256), Image.LANCZOS).save(out + ".png")
img.save(out + ".ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
