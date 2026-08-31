using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

class MakeIco {
  static Bitmap LoadCrop(string path) {
    using (var src = new Bitmap(path)) {
      int w = src.Width, h = src.Height;
      int l = w, t = h, r = 0, b = 0;
      for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++) {
          Color c = src.GetPixel(x, y);
          if (c.R + c.G + c.B > 28) {
            if (x < l) l = x; if (y < t) t = y;
            if (x > r) r = x; if (y > b) b = y;
          }
        }
      int pad = 4;
      l = Math.Max(0, l - pad); t = Math.Max(0, t - pad);
      r = Math.Min(w - 1, r + pad); b = Math.Min(h - 1, b + pad);
      var crop = new Bitmap(r - l + 1, b - t + 1, PixelFormat.Format32bppArgb);
      using (var g = Graphics.FromImage(crop)) {
        g.DrawImage(src, new Rectangle(0, 0, crop.Width, crop.Height),
          new Rectangle(l, t, crop.Width, crop.Height), GraphicsUnit.Pixel);
      }
      return crop;
    }
  }

  static Bitmap Scale(Bitmap src, int size) {
    var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(dst)) {
      g.InterpolationMode = InterpolationMode.HighQualityBicubic;
      g.SmoothingMode = SmoothingMode.HighQuality;
      g.PixelOffsetMode = PixelOffsetMode.HighQuality;
      g.Clear(Color.Transparent);
      g.DrawImage(src, 0, 0, size, size);
    }
    return dst;
  }

  static byte[] Bmp32(Bitmap bmp) {
    var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
    var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    int stride = bmp.Width * 4;
    int andRow = ((bmp.Width + 31) / 32) * 4;
    int xorSize = stride * bmp.Height;
    int andSize = andRow * bmp.Height;
    var buf = new byte[40 + xorSize + andSize];
    // BITMAPINFOHEADER
    BitConverter.GetBytes(40).CopyTo(buf, 0);
    BitConverter.GetBytes(bmp.Width).CopyTo(buf, 4);
    BitConverter.GetBytes(bmp.Height * 2).CopyTo(buf, 8);
    BitConverter.GetBytes((short)1).CopyTo(buf, 12);
    BitConverter.GetBytes((short)32).CopyTo(buf, 14);
    BitConverter.GetBytes(xorSize).CopyTo(buf, 20);
    byte[] row = new byte[stride];
    for (int y = 0; y < bmp.Height; y++) {
      Marshal.Copy(IntPtr.Add(data.Scan0, (bmp.Height - 1 - y) * data.Stride), row, 0, stride);
      Buffer.BlockCopy(row, 0, buf, 40 + y * stride, stride);
    }
    bmp.UnlockBits(data);
    return buf;
  }

  static void WriteIco(string outPath, Bitmap master) {
    int[] sizes = { 16, 32, 48, 256 };
    var parts = new byte[sizes.Length][];
    for (int i = 0; i < sizes.Length; i++) {
      using (var s = Scale(master, sizes[i]))
        parts[i] = Bmp32(s);
    }
    int offset = 6 + 16 * sizes.Length;
    using (var fs = File.Create(outPath))
    using (var bw = new BinaryWriter(fs)) {
      bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
      int pos = offset;
      for (int i = 0; i < sizes.Length; i++) {
        int sz = sizes[i];
        bw.Write((byte)(sz == 256 ? 0 : sz));
        bw.Write((byte)(sz == 256 ? 0 : sz));
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write((short)1); bw.Write((short)32);
        bw.Write(parts[i].Length);
        bw.Write(pos);
        pos += parts[i].Length;
      }
      for (int i = 0; i < sizes.Length; i++) bw.Write(parts[i]);
    }
  }

  static void Main(string[] args) {
    string dir = args[0];
    string[] names = { "app", "search", "inline" };
    string[] srcs = { args[1], args[2], args[3] };
    Directory.CreateDirectory(Path.Combine(dir, "assets"));
    for (int i = 0; i < 3; i++) {
      using (var crop = LoadCrop(srcs[i])) {
        string png = Path.Combine(dir, "assets", names[i] + ".png");
        using (var s256 = Scale(crop, 256))
          s256.Save(png, ImageFormat.Png);
        using (var s32 = Scale(crop, 32))
          s32.Save(Path.Combine(dir, "assets", names[i] + "32.png"), ImageFormat.Png);
        WriteIco(Path.Combine(dir, "assets", names[i] + ".ico"), crop);
        Console.WriteLine("wrote " + names[i]);
      }
    }
  }
}
