using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeviceScanner.UI
{
    /// <summary>A turning arc while a module runs; a coloured disc with ✓ / ! / ✕ once the run is over.</summary>
    internal sealed class Spinner : Control
    {
        private readonly Timer _timer = new Timer { Interval = 30 };
        private int _angle;
        private Color? _doneColor;
        private string _doneGlyph;

        public Color Track { get; set; } = Color.FromArgb(0xD5, 0xDB, 0xE1);
        public Color Accent { get; set; } = Color.FromArgb(0x1F, 0x3A, 0x5F);

        public Spinner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            _timer.Tick += (s, e) => { _angle = (_angle + 10) % 360; Invalidate(); };
        }

        public void Start()
        {
            _doneColor = null;
            _timer.Start();
            Invalidate();
        }

        public void Stop(Color color, string glyph)
        {
            _timer.Stop();
            _doneColor = color;
            _doneGlyph = glyph;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float thick = Math.Max(3, Width / 10f);
            var r = new RectangleF(thick / 2 + 1, thick / 2 + 1, Width - thick - 2, Height - thick - 2);

            if (_doneColor.HasValue)
            {
                using (var fill = new SolidBrush(_doneColor.Value)) g.FillEllipse(fill, r);
                using (var font = new Font("Segoe UI Symbol", Height * 0.33f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(_doneGlyph, font, Brushes.White, r, fmt);
                return;
            }

            using (var track = new Pen(Track, thick)) g.DrawEllipse(track, r);
            using (var arc = new Pen(Accent, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(arc, r, _angle, 100);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
