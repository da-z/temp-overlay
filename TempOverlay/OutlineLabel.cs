using System;
using System.Drawing;
using System.Windows.Forms;

namespace TempOverlay
{
    internal sealed class OutlineLabel : Control
    {
        private Color _outlineColor = Color.Black;
        private int _outlineWidth = 0;

        public Color OutlineColor
        {
            get => _outlineColor;
            set
            {
                if (_outlineColor == value)
                {
                    return;
                }

                _outlineColor = value;
                Invalidate();
            }
        }

        public int OutlineWidth
        {
            get => _outlineWidth;
            set
            {
                var safeValue = value < 0 ? 0 : value;
                if (_outlineWidth == safeValue)
                {
                    return;
                }

                _outlineWidth = safeValue;
                AdjustSize();
                Invalidate();
            }
        }

        public override string Text
        {
            get => base.Text;
            set
            {
                if (base.Text == value)
                {
                    return;
                }

                base.Text = value;
                AdjustSize();
                Invalidate();
            }
        }

        public override Font Font
        {
            get => base.Font;
            set
            {
                if (base.Font == value)
                {
                    return;
                }

                base.Font = value;
                AdjustSize();
                Invalidate();
            }
        }

        public OutlineLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            Font = new Font("Tektur", 18, FontStyle.Bold);
            Text = string.Empty;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (string.IsNullOrEmpty(Text))
            {
                return;
            }

            FontSpriteAtlas.DrawText(e.Graphics, Text, new Point(0, 0), Font, ForeColor, OutlineColor, OutlineWidth);
        }

        private void AdjustSize()
        {
            if (string.IsNullOrEmpty(Text))
            {
                Size = new Size(1, Font.Height);
                return;
            }

            var size = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            Width = Math.Max(1, size.Width + (OutlineWidth * 2) + 4);
            Height = Math.Max(1, size.Height + (OutlineWidth * 2) + 4);
        }
    }
}
