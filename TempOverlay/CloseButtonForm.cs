using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TempOverlay
{
    internal sealed class CloseButtonForm : Form
    {
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);

        private readonly string _fontFamily;
        private readonly int _buttonSize;
        private bool _isHovered;

        public event EventHandler ButtonClicked;

        public bool IsPointerOver => Visible && Bounds.Contains(Cursor.Position);

        public CloseButtonForm(string fontFamily, int buttonSize)
        {
            _fontFamily = fontFamily;
            _buttonSize = Math.Max(10, buttonSize);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Size = new Size(_buttonSize, _buttonSize);
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExNoActivate | WsExToolWindow;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        public void ShowAt(int x, int y)
        {
            Location = new Point(x, y);
            if (!Visible)
            {
                Show();
            }

            EnsureTopMost();
            Invalidate();
        }

        public void HideButton()
        {
            if (Visible)
            {
                Hide();
            }
        }

        public void EnsureTopMost()
        {
            if (!IsHandleCreated || !Visible)
            {
                return;
            }

            _ = SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                ButtonClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var fill = new SolidBrush(_isHovered ? Color.FromArgb(220, 32, 32) : Color.White))
            using (var textBrush = new SolidBrush(_isHovered ? Color.White : Color.Black))
            using (var font = new Font(_fontFamily, 8f, FontStyle.Bold))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                e.Graphics.FillEllipse(fill, rect);
                var glyphRect = new Rectangle(rect.Left + 1, rect.Top, rect.Width, rect.Height);
                e.Graphics.DrawString("x", font, textBrush, glyphRect, sf);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    }
}
