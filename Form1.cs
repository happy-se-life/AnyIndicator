using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

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
        private enum CaptureAreaPreset
        {
            Size12,
            Size24
        }
        private enum LedSizePreset
        {
            Small,
            Medium,
            Large
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
        private readonly ToolStripMenuItem captureSize12MenuItem;
        private readonly ToolStripMenuItem captureSize24MenuItem;
        private readonly ToolStripMenuItem ledSizeSmallMenuItem;
        private readonly ToolStripMenuItem ledSizeMediumMenuItem;
        private readonly ToolStripMenuItem ledSizeLargeMenuItem;
        private readonly string stateFilePath;
        private readonly string baselineImagePath;
        private bool isLedOn = true;
        private bool showLed;
        private bool isCaptureMode;
        private bool isCapturePending;
        private bool lastLeftButtonDown;
        private Point ledScreenCenter;
        private LedPalette currentPalette = LedPalette.Blue;
        private BlinkSpeedPreset currentBlinkSpeed = BlinkSpeedPreset.Normal;
        private CaptureAreaPreset currentCaptureArea = CaptureAreaPreset.Size12;
        private LedSizePreset currentLedSize = LedSizePreset.Medium;
        private Point watchedScreenPoint;
        private Point pendingCapturePoint;
        private Bitmap? baselineCapture;
        private long lastCompareTickMs;
        private long pendingCaptureDueMs;

        public Form1()
        {
            InitializeComponent();

            stateFilePath = Path.Combine(Application.UserAppDataPath, "app-state.json");
            baselineImagePath = Path.Combine(Application.UserAppDataPath, "baseline-capture.png");

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

            ToolStripMenuItem ledSizeMenuItem = new("LEDサイズ");
            ledSizeSmallMenuItem = new ToolStripMenuItem("小", null, (_, _) => SetLedSize(LedSizePreset.Small));
            ledSizeMediumMenuItem = new ToolStripMenuItem("中", null, (_, _) => SetLedSize(LedSizePreset.Medium));
            ledSizeLargeMenuItem = new ToolStripMenuItem("大", null, (_, _) => SetLedSize(LedSizePreset.Large));
            ledSizeMenuItem.DropDownItems.Add(ledSizeLargeMenuItem);
            ledSizeMenuItem.DropDownItems.Add(ledSizeMediumMenuItem);
            ledSizeMenuItem.DropDownItems.Add(ledSizeSmallMenuItem);
            trayMenu.Items.Add(ledSizeMenuItem);
            trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem captureSizeMenuItem = new("キャプチャ領域");
            captureSize12MenuItem = new ToolStripMenuItem("12x12", null, (_, _) => SetCaptureArea(CaptureAreaPreset.Size12));
            captureSize24MenuItem = new ToolStripMenuItem("24x24", null, (_, _) => SetCaptureArea(CaptureAreaPreset.Size24));
            captureSizeMenuItem.DropDownItems.Add(captureSize12MenuItem);
            captureSizeMenuItem.DropDownItems.Add(captureSize24MenuItem);
            trayMenu.Items.Add(captureSizeMenuItem);
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

            LoadAppState();
            ApplyBlinkSpeedInterval();
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
                SaveAppState();
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
            }

            lastLeftButtonDown = isLeftButtonDown;

            long nowMs = Environment.TickCount64;
            if (isCapturePending && nowMs >= pendingCaptureDueMs)
            {
                SaveBaselineCapture(pendingCapturePoint);
                isCapturePending = false;
                if (baselineCapture is not null)
                {
                    ShowCapturePreview(baselineCapture, pendingCapturePoint);
                }
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
        }

        private void SaveBaselineCapture(Point clickPoint)
        {
            baselineCapture?.Dispose();
            baselineCapture = CaptureAreaAround(clickPoint);
            watchedScreenPoint = clickPoint;
            showLed = false;
            RenderLedOverlay();
            SaveAppState();
        }

        private static void ShowCapturePreview(Bitmap capture, Point clickPoint)
        {
            CapturePreviewForm previewForm = new((Bitmap)capture.Clone(), clickPoint);
            previewForm.Show();
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

        private Bitmap CaptureAreaAround(Point center)
        {
            int captureSize = GetCaptureSize();
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            int left = center.X - (captureSize / 2);
            int top = center.Y - (captureSize / 2);

            left = Math.Clamp(left, virtualScreen.Left, virtualScreen.Right - captureSize);
            top = Math.Clamp(top, virtualScreen.Top, virtualScreen.Bottom - captureSize);

            using Bitmap source = new(captureSize, captureSize, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(source))
            {
                g.CopyFromScreen(left, top, 0, 0, new Size(captureSize, captureSize), CopyPixelOperation.SourceCopy);
            }

            return (Bitmap)source.Clone();
        }

        private int GetCaptureSize()
        {
            return currentCaptureArea == CaptureAreaPreset.Size24 ? 24 : 12;
        }

        private void SetLedPalette(LedPalette palette)
        {
            currentPalette = palette;
            UpdateTrayMenuChecks();
            RenderLedOverlay();
            SaveAppState();
        }

        private void SetBlinkSpeed(BlinkSpeedPreset speed)
        {
            currentBlinkSpeed = speed;
            ApplyBlinkSpeedInterval();
            UpdateTrayMenuChecks();
            SaveAppState();
        }

        private void SetCaptureArea(CaptureAreaPreset preset)
        {
            currentCaptureArea = preset;
            baselineCapture?.Dispose();
            baselineCapture = null;
            showLed = false;
            RenderLedOverlay();
            UpdateTrayMenuChecks();
            SaveAppState();
        }

        private void SetLedSize(LedSizePreset preset)
        {
            currentLedSize = preset;
            UpdateTrayMenuChecks();
            RenderLedOverlay();
            SaveAppState();
        }

        private void ApplyBlinkSpeedInterval()
        {
            blinkTimer.Interval = currentBlinkSpeed switch
            {
                BlinkSpeedPreset.Slow => 800,
                BlinkSpeedPreset.Fast => 200,
                _ => 450
            };
        }

        private void LoadAppState()
        {
            try
            {
                if (!File.Exists(stateFilePath))
                {
                    return;
                }

                string json = File.ReadAllText(stateFilePath);
                PersistedState? state = JsonSerializer.Deserialize<PersistedState>(json);
                if (state is null)
                {
                    return;
                }

                if (Enum.TryParse(state.Palette, out LedPalette palette))
                {
                    currentPalette = palette;
                }

                if (Enum.TryParse(state.BlinkSpeed, out BlinkSpeedPreset blinkSpeed))
                {
                    currentBlinkSpeed = blinkSpeed;
                }

                if (Enum.TryParse(state.CaptureArea, out CaptureAreaPreset captureArea))
                {
                    currentCaptureArea = captureArea;
                }

                if (Enum.TryParse(state.LedSize, out LedSizePreset ledSize))
                {
                    currentLedSize = ledSize;
                }

                if (state.HasBaselineCapture && File.Exists(baselineImagePath))
                {
                    using Image image = Image.FromFile(baselineImagePath);
                    baselineCapture?.Dispose();
                    baselineCapture = new Bitmap(image);
                    watchedScreenPoint = new Point(state.WatchedX, state.WatchedY);
                }
            }
            catch
            {
                // 読み込み失敗時はデフォルト設定で起動する
            }
        }

        private void SaveAppState()
        {
            try
            {
                Directory.CreateDirectory(Application.UserAppDataPath);
                if (baselineCapture is not null)
                {
                    baselineCapture.Save(baselineImagePath, ImageFormat.Png);
                }
                else if (File.Exists(baselineImagePath))
                {
                    File.Delete(baselineImagePath);
                }

                PersistedState state = new()
                {
                    Palette = currentPalette.ToString(),
                    BlinkSpeed = currentBlinkSpeed.ToString(),
                    CaptureArea = currentCaptureArea.ToString(),
                    LedSize = currentLedSize.ToString(),
                    WatchedX = watchedScreenPoint.X,
                    WatchedY = watchedScreenPoint.Y,
                    HasBaselineCapture = baselineCapture is not null
                };

                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(stateFilePath, json);
            }
            catch
            {
                // 保存失敗時もアプリ本体の動作は継続する
            }
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
            ledSizeSmallMenuItem.Checked = currentLedSize == LedSizePreset.Small;
            ledSizeMediumMenuItem.Checked = currentLedSize == LedSizePreset.Medium;
            ledSizeLargeMenuItem.Checked = currentLedSize == LedSizePreset.Large;
            captureSize12MenuItem.Checked = currentCaptureArea == CaptureAreaPreset.Size12;
            captureSize24MenuItem.Checked = currentCaptureArea == CaptureAreaPreset.Size24;
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

            int diameter = currentLedSize switch
            {
                LedSizePreset.Small => 8,
                LedSizePreset.Large => 14,
                _ => 10
            };
            int ringPadding = currentLedSize == LedSizePreset.Large ? 2 : 1;
            int glowPadding = currentLedSize switch
            {
                LedSizePreset.Small => 2,
                LedSizePreset.Large => 4,
                _ => 3
            };
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
                    int highlightWidth = Math.Max(2, diameter / 3);
                    int highlightHeight = Math.Max(1, diameter / 4);
                    Rectangle highlightRect = new(x + 2, y + 1, highlightWidth, highlightHeight);
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

        private sealed class CapturePreviewForm : Form
        {
            private readonly Bitmap previewBitmap;
            private readonly System.Windows.Forms.Timer closeTimer;

            public CapturePreviewForm(Bitmap sourceBitmap, Point clickPoint)
            {
                previewBitmap = sourceBitmap;
                FormBorderStyle = FormBorderStyle.FixedToolWindow;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                Text = $"Capture {sourceBitmap.Width}x{sourceBitmap.Height}";
                ClientSize = new Size(120, 120);

                int offset = 16;
                Rectangle virtualScreen = SystemInformation.VirtualScreen;
                int x = Math.Clamp(clickPoint.X + offset, virtualScreen.Left, virtualScreen.Right - Width);
                int y = Math.Clamp(clickPoint.Y + offset, virtualScreen.Top, virtualScreen.Bottom - Height);
                Location = new Point(x, y);

                closeTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                closeTimer.Tick += (_, _) => Close();
                closeTimer.Start();

                FormClosed += (_, _) =>
                {
                    closeTimer.Dispose();
                    previewBitmap.Dispose();
                };
            }

            protected override bool ShowWithoutActivation => true;

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                e.Graphics.Clear(Color.FromArgb(24, 24, 24));

                Rectangle targetRect = new(10, 10, ClientSize.Width - 20, ClientSize.Height - 20);
                e.Graphics.DrawImage(previewBitmap, targetRect);
                using Pen border = new(Color.White);
                e.Graphics.DrawRectangle(border, targetRect);
            }
        }

        private sealed class PersistedState
        {
            public string Palette { get; set; } = LedPalette.Blue.ToString();
            public string BlinkSpeed { get; set; } = BlinkSpeedPreset.Normal.ToString();
            public string CaptureArea { get; set; } = CaptureAreaPreset.Size12.ToString();
            public string LedSize { get; set; } = LedSizePreset.Medium.ToString();
            public int WatchedX { get; set; }
            public int WatchedY { get; set; }
            public bool HasBaselineCapture { get; set; }
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
