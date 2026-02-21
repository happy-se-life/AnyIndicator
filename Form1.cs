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
        private enum BlinkSpeedPreset
        {
            Slow,
            Normal,
            Fast
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
        private const int VK_LBUTTON = 0x01;
        private const int CaptureSize = 10;
        private const int CaptureDelayMs = 2000;
        private const int CompareIntervalMs = 120;

        private readonly System.Windows.Forms.Timer blinkTimer;
        private readonly System.Windows.Forms.Timer monitorTimer;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private readonly ToolStripMenuItem blueMenuItem;
        private readonly ToolStripMenuItem redMenuItem;
        private readonly ToolStripMenuItem yellowMenuItem;
        private readonly ToolStripMenuItem greenMenuItem;
        private readonly ToolStripMenuItem slowSpeedMenuItem;
        private readonly ToolStripMenuItem normalSpeedMenuItem;
        private readonly ToolStripMenuItem fastSpeedMenuItem;
        private bool isLedOn = true;
        private bool showLed;
        private bool isCaptureMode;
        private bool isCapturePending;
        private bool lastLeftButtonDown;
        private Point ledScreenCenter;
        private LedPalette currentPalette = LedPalette.Blue;
        private BlinkSpeedPreset currentBlinkSpeed = BlinkSpeedPreset.Normal;
        private Point watchedScreenPoint;
        private Point pendingCapturePoint;
        private Bitmap? baselineCapture;
        private long lastCompareTickMs;
        private long pendingCaptureDueMs;

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

            monitorTimer = new System.Windows.Forms.Timer
            {
                Interval = 10
            };
            monitorTimer.Tick += MonitorTimer_Tick;

            trayMenu = new ContextMenuStrip();
            blueMenuItem = new ToolStripMenuItem("青", null, (_, _) => SetLedPalette(LedPalette.Blue));
            redMenuItem = new ToolStripMenuItem("赤", null, (_, _) => SetLedPalette(LedPalette.Red));
            yellowMenuItem = new ToolStripMenuItem("黄", null, (_, _) => SetLedPalette(LedPalette.Yellow));
            greenMenuItem = new ToolStripMenuItem("緑", null, (_, _) => SetLedPalette(LedPalette.Green));

            ToolStripMenuItem colorMenuItem = new("色");
            colorMenuItem.DropDownItems.Add(blueMenuItem);
            colorMenuItem.DropDownItems.Add(redMenuItem);
            colorMenuItem.DropDownItems.Add(yellowMenuItem);
            colorMenuItem.DropDownItems.Add(greenMenuItem);
            trayMenu.Items.Add(colorMenuItem);
            trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem speedMenuItem = new("点滅速度");
            slowSpeedMenuItem = new ToolStripMenuItem("遅い", null, (_, _) => SetBlinkSpeed(BlinkSpeedPreset.Slow));
            normalSpeedMenuItem = new ToolStripMenuItem("標準", null, (_, _) => SetBlinkSpeed(BlinkSpeedPreset.Normal));
            fastSpeedMenuItem = new ToolStripMenuItem("速い", null, (_, _) => SetBlinkSpeed(BlinkSpeedPreset.Fast));
            speedMenuItem.DropDownItems.Add(slowSpeedMenuItem);
            speedMenuItem.DropDownItems.Add(normalSpeedMenuItem);
            speedMenuItem.DropDownItems.Add(fastSpeedMenuItem);
            trayMenu.Items.Add(speedMenuItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("キャプチャ", null, (_, _) => StartCaptureMode());
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
                monitorTimer.Start();
                RenderLedOverlay();
            };

            FormClosed += (_, _) =>
            {
                blinkTimer.Dispose();
                monitorTimer.Dispose();
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayMenu.Dispose();
                baselineCapture?.Dispose();
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
            if (showLed)
            {
                RenderLedOverlay();
            }
        }

        private void MonitorTimer_Tick(object? sender, EventArgs e)
        {
            Point cursorPos = Cursor.Position;
            bool isLeftButtonDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

            if (isCaptureMode && isLeftButtonDown && !lastLeftButtonDown)
            {
                isCaptureMode = false;
                isCapturePending = true;
                pendingCapturePoint = cursorPos;
                pendingCaptureDueMs = Environment.TickCount64 + CaptureDelayMs;
                trayIcon.ShowBalloonTip(1200, "キャプチャ待機", "2秒後に基準画像を保存します。", ToolTipIcon.Info);
            }

            lastLeftButtonDown = isLeftButtonDown;

            long nowMs = Environment.TickCount64;
            if (isCapturePending && nowMs >= pendingCaptureDueMs)
            {
                SaveBaselineCapture(pendingCapturePoint);
                isCapturePending = false;
                trayIcon.ShowBalloonTip(1000, "キャプチャ完了", "監視を開始しました。", ToolTipIcon.Info);
            }

            if (baselineCapture is null || isCaptureMode || isCapturePending)
            {
                return;
            }

            if (nowMs - lastCompareTickMs < CompareIntervalMs)
            {
                if (showLed && cursorPos != ledScreenCenter)
                {
                    ledScreenCenter = cursorPos;
                    RenderLedOverlay();
                }
                return;
            }

            lastCompareTickMs = nowMs;
            bool changed = HasScreenChanged();
            if (changed != showLed)
            {
                showLed = changed;
                if (showLed)
                {
                    ledScreenCenter = cursorPos;
                }
                RenderLedOverlay();
                return;
            }

            if (showLed && cursorPos != ledScreenCenter)
            {
                ledScreenCenter = cursorPos;
                RenderLedOverlay();
            }
        }

        private void StartCaptureMode()
        {
            isCaptureMode = true;
            isCapturePending = false;
            showLed = false;
            RenderLedOverlay();
            trayIcon.ShowBalloonTip(1500, "キャプチャモード", "監視したい場所を左クリックしてください（2秒後に保存）。", ToolTipIcon.Info);
        }

        private void SaveBaselineCapture(Point clickPoint)
        {
            baselineCapture?.Dispose();
            baselineCapture = CaptureAreaAround(clickPoint);
            watchedScreenPoint = clickPoint;
            showLed = false;
            RenderLedOverlay();
        }

        private bool HasScreenChanged()
        {
            if (baselineCapture is null)
            {
                return false;
            }

            using Bitmap current = CaptureAreaAround(watchedScreenPoint);
            return !AreBitmapsEqual(baselineCapture, current);
        }

        private static bool AreBitmapsEqual(Bitmap first, Bitmap second)
        {
            if (first.Width != second.Width || first.Height != second.Height)
            {
                return false;
            }

            for (int y = 0; y < first.Height; y++)
            {
                for (int x = 0; x < first.Width; x++)
                {
                    if (first.GetPixel(x, y).ToArgb() != second.GetPixel(x, y).ToArgb())
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static Bitmap CaptureAreaAround(Point center)
        {
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            int left = center.X - (CaptureSize / 2);
            int top = center.Y - (CaptureSize / 2);

            left = Math.Clamp(left, virtualScreen.Left, virtualScreen.Right - CaptureSize);
            top = Math.Clamp(top, virtualScreen.Top, virtualScreen.Bottom - CaptureSize);

            using Bitmap source = new(CaptureSize, CaptureSize, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(source))
            {
                g.CopyFromScreen(left, top, 0, 0, new Size(CaptureSize, CaptureSize), CopyPixelOperation.SourceCopy);
            }

            return (Bitmap)source.Clone();
        }

        private void SetLedPalette(LedPalette palette)
        {
            currentPalette = palette;
            UpdateTrayMenuChecks();
            RenderLedOverlay();
        }

        private void SetBlinkSpeed(BlinkSpeedPreset speed)
        {
            currentBlinkSpeed = speed;
            blinkTimer.Interval = speed switch
            {
                BlinkSpeedPreset.Slow => 800,
                BlinkSpeedPreset.Fast => 200,
                _ => 450
            };
            UpdateTrayMenuChecks();
        }

        private void UpdateTrayMenuChecks()
        {
            blueMenuItem.Checked = currentPalette == LedPalette.Blue;
            redMenuItem.Checked = currentPalette == LedPalette.Red;
            yellowMenuItem.Checked = currentPalette == LedPalette.Yellow;
            greenMenuItem.Checked = currentPalette == LedPalette.Green;
            slowSpeedMenuItem.Checked = currentBlinkSpeed == BlinkSpeedPreset.Slow;
            normalSpeedMenuItem.Checked = currentBlinkSpeed == BlinkSpeedPreset.Normal;
            fastSpeedMenuItem.Checked = currentBlinkSpeed == BlinkSpeedPreset.Fast;
        }

        private void RenderLedOverlay()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            if (!showLed)
            {
                using Bitmap hidden = new(1, 1, PixelFormat.Format32bppArgb);
                hidden.SetPixel(0, 0, Color.Transparent);
                UpdateLayeredBitmap(hidden, new Point(-32000, -32000));
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

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
