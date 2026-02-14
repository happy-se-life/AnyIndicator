using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AnyIndicator
{
    public partial class Form1 : Form
    {
        private enum LedPalette
        {
            Blue,
            Red,
            Yellow,
            Green
        }

        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        private readonly System.Windows.Forms.Timer blinkTimer;
        private readonly System.Windows.Forms.Timer followTimer;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private bool isLedOn = true;
        private Point ledScreenCenter;
        private LedPalette currentPalette = LedPalette.Blue;

        public Form1()
        {
            InitializeComponent();

            Text = string.Empty;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Size = new Size(1, 1);
            Location = new Point(-32000, -32000);

            blinkTimer = new System.Windows.Forms.Timer
            {
                Interval = 450
            };
            blinkTimer.Tick += BlinkTimer_Tick;

            followTimer = new System.Windows.Forms.Timer
            {
                Interval = 10
            };
            followTimer.Tick += FollowTimer_Tick;

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("青", null, (_, _) => SetLedPalette(LedPalette.Blue));
            trayMenu.Items.Add("赤", null, (_, _) => SetLedPalette(LedPalette.Red));
            trayMenu.Items.Add("黄", null, (_, _) => SetLedPalette(LedPalette.Yellow));
            trayMenu.Items.Add("緑", null, (_, _) => SetLedPalette(LedPalette.Green));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("終了", null, (_, _) => Application.Exit());

            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Text = "AnyIndicator - 色を選択",
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            UpdateTrayMenuChecks();

            ledScreenCenter = Cursor.Position;
            Shown += (_, _) =>
            {
                blinkTimer.Start();
                followTimer.Start();
                RenderLedOverlay();
            };

            FormClosed += (_, _) =>
            {
                blinkTimer.Dispose();
                followTimer.Dispose();
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayMenu.Dispose();
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }

            base.WndProc(ref m);
        }

        private void BlinkTimer_Tick(object? sender, EventArgs e)
        {
            isLedOn = !isLedOn;
            RenderLedOverlay();
        }

        private void FollowTimer_Tick(object? sender, EventArgs e)
        {
            Point cursorPos = Cursor.Position;
            if (cursorPos != ledScreenCenter)
            {
                ledScreenCenter = cursorPos;
                RenderLedOverlay();
            }
        }

        private void SetLedPalette(LedPalette palette)
        {
            currentPalette = palette;
            UpdateTrayMenuChecks();
            RenderLedOverlay();
        }

        private void UpdateTrayMenuChecks()
        {
            foreach (ToolStripItem item in trayMenu.Items)
            {
                if (item is ToolStripMenuItem menuItem)
                {
                    menuItem.Checked = menuItem.Text switch
                    {
                        "青" => currentPalette == LedPalette.Blue,
                        "赤" => currentPalette == LedPalette.Red,
                        "黄" => currentPalette == LedPalette.Yellow,
                        "緑" => currentPalette == LedPalette.Green,
                        _ => false
                    };
                }
            }
        }

        private void RenderLedOverlay()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            const int diameter = 10;
            const int ringPadding = 1;
            const int glowPadding = 3;
            int overlaySize = diameter + (glowPadding * 2);
            int x = glowPadding;
            int y = glowPadding;

            Rectangle ledRect = new(x, y, diameter, diameter);
            Rectangle glowRect = new(0, 0, overlaySize, overlaySize);

            (Color onColor, Color offColor) = currentPalette switch
            {
                LedPalette.Red => (
                    Color.FromArgb(255, 60, 40),
                    Color.FromArgb(50, 85, 30, 25)
                ),
                LedPalette.Yellow => (
                    Color.FromArgb(255, 215, 40),
                    Color.FromArgb(50, 95, 80, 25)
                ),
                LedPalette.Green => (
                    Color.FromArgb(60, 220, 90),
                    Color.FromArgb(50, 25, 75, 35)
                ),
                _ => (
                    Color.FromArgb(0, 120, 255),
                    Color.FromArgb(50, 25, 45, 85)
                )
            };

            Color glowColor = Color.FromArgb(100, onColor.R, onColor.G, onColor.B);
            Color ringColor = Color.FromArgb(
                255,
                Math.Min(255, onColor.R + 80),
                Math.Min(255, onColor.G + 80),
                Math.Min(255, onColor.B + 80)
            );
            Color ledColor = isLedOn ? onColor : offColor;

            using Bitmap bitmap = new(overlaySize, overlaySize, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                if (isLedOn)
                {
                    using SolidBrush glowBrush = new(glowColor);
                    g.FillEllipse(glowBrush, glowRect);
                }

                using SolidBrush ledBrush = new(ledColor);
                g.FillEllipse(ledBrush, ledRect);

                using Pen ringPen = new(ringColor, ringPadding);
                g.DrawEllipse(ringPen, ledRect);

                if (isLedOn)
                {
                    Rectangle highlightRect = new(x + 2, y + 1, 3, 2);
                    using SolidBrush highlightBrush = new(Color.FromArgb(150, 255, 255, 255));
                    g.FillEllipse(highlightBrush, highlightRect);
                }
            }

            Point topLeft = new(ledScreenCenter.X - (overlaySize / 2), ledScreenCenter.Y - (overlaySize / 2));
            UpdateLayeredBitmap(bitmap, topLeft);
        }

        private void UpdateLayeredBitmap(Bitmap bitmap, Point topLeft)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = SelectObject(memDc, hBitmap);

            try
            {
                SIZE size = new(bitmap.Width, bitmap.Height);
                POINT sourcePoint = new(0, 0);
                POINT topPos = new(topLeft.X, topLeft.Y);
                BLENDFUNCTION blend = new()
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(
                    Handle,
                    screenDc,
                    ref topPos,
                    ref size,
                    memDc,
                    ref sourcePoint,
                    0,
                    ref blend,
                    ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int CX;
            public int CY;

            public SIZE(int cx, int cy)
            {
                CX = cx;
                CY = cy;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hWnd,
            IntPtr hdcDst,
            ref POINT pptDst,
            ref SIZE psize,
            IntPtr hdcSrc,
            ref POINT pptSrc,
            int crKey,
            ref BLENDFUNCTION pblend,
            int dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr ho);
    }
}
