namespace AnyIndicator
{
    public partial class Form1 : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly System.Windows.Forms.Timer blinkTimer;
        private readonly System.Windows.Forms.Timer followTimer;
        private bool isLedOn = true;
        private Point ledCenter;

        public Form1()
        {
            InitializeComponent();

            Text = string.Empty;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            ShowInTaskbar = false;

            blinkTimer = new System.Windows.Forms.Timer
            {
                Interval = 450
            };
            blinkTimer.Tick += BlinkTimer_Tick;
            blinkTimer.Start();

            followTimer = new System.Windows.Forms.Timer
            {
                Interval = 16
            };
            followTimer.Tick += FollowTimer_Tick;
            followTimer.Start();

            ledCenter = new Point(ClientSize.Width / 2, ClientSize.Height / 2);

            FormClosed += (_, _) =>
            {
                blinkTimer.Dispose();
                followTimer.Dispose();
            };
            Resize += (_, _) => Invalidate();
            Paint += Form1_Paint;
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
            Invalidate();
        }

        private void FollowTimer_Tick(object? sender, EventArgs e)
        {
            Point cursorInClient = PointToClient(Cursor.Position);
            Point newCenter = new(cursorInClient.X, cursorInClient.Y);

            if (newCenter != ledCenter)
            {
                ledCenter = newCenter;
                Invalidate();
            }
        }

        private void Form1_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            const int diameter = 10;
            const int ringPadding = 1;
            int x = ledCenter.X - (diameter / 2);
            int y = ledCenter.Y - (diameter / 2);
            Rectangle ledRect = new(x, y, diameter, diameter);
            Rectangle glowRect = new(x - 4, y - 4, diameter + 8, diameter + 8);

            if (isLedOn)
            {
                using SolidBrush glowBrush = new(Color.FromArgb(95, 0, 140, 255));
                e.Graphics.FillEllipse(glowBrush, glowRect);
            }

            Color ledColor = isLedOn ? Color.FromArgb(0, 120, 255) : Color.FromArgb(25, 45, 85);
            using SolidBrush ledBrush = new(ledColor);
            e.Graphics.FillEllipse(ledBrush, ledRect);

            using Pen ringPen = new(Color.FromArgb(160, 220, 255), ringPadding);
            e.Graphics.DrawEllipse(ringPen, ledRect);

            if (isLedOn)
            {
                Rectangle highlightRect = new(x + 2, y + 1, 3, 2);
                using SolidBrush highlightBrush = new(Color.FromArgb(150, 255, 255, 255));
                e.Graphics.FillEllipse(highlightBrush, highlightRect);
            }
        }
    }
}
