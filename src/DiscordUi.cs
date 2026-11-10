using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiscordDmCleaner {
    internal static class Theme {
        public static readonly Color Canvas = Color.FromArgb(49, 51, 56);
        public static readonly Color Card = Color.FromArgb(43, 45, 49);
        public static readonly Color Rail = Color.FromArgb(30, 31, 34);
        public static readonly Color Input = Color.FromArgb(30, 31, 34);
        public static readonly Color Text = Color.FromArgb(242, 243, 245);
        public static readonly Color Muted = Color.FromArgb(181, 186, 193);
        public static readonly Color Faint = Color.FromArgb(148, 155, 164);
        public static readonly Color Blurple = Color.FromArgb(88, 101, 242);
        public static readonly Color Danger = Color.FromArgb(218, 55, 60);
        public static readonly Color Green = Color.FromArgb(63, 196, 131);
        public static readonly Color Gold = Color.FromArgb(240, 178, 50);
        public static GraphicsPath Round(RectangleF bounds, float radius) {
            var path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
        public static Label Label(Control parent, string text, int x, int y, int w, int h, float size, Color color, bool bold = false) {
            var label = new Label { Text = text, Bounds = new Rectangle(x,y,w,h), BackColor = parent.BackColor,
                ForeColor = color, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), AutoEllipsis = true };
            parent.Controls.Add(label); return label;
        }
        public static TextBox Edit(RoundPanel panel, int width, bool secret) {
            var box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Input, ForeColor = Text,
                Font = new Font("Segoe UI", 10.5f), UseSystemPasswordChar = secret, Bounds = new Rectangle(14, 12, width - 28, 24) };
            box.Enter += delegate { panel.LineColor = Blurple; panel.Invalidate(); };
            box.Leave += delegate { panel.LineColor = Color.Empty; panel.Invalidate(); };
            panel.Controls.Add(box); return box;
        }
        public static Icon MakeIcon() {
            using (var stream = typeof(Theme).Assembly.GetManifestResourceStream("DiscordDmCleaner.Icon")) {
                if (stream == null) throw new InvalidOperationException("Application icon resource is missing.");
                using (var icon = new Icon(stream)) return (Icon)icon.Clone();
            }
        }
    }
    internal class RoundPanel : Panel {
        public int Radius = 12;
        public Color LineColor = Color.Empty;
        public RoundPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent == null ? Theme.Canvas : Parent.BackColor); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(0.5f,0.5f,Width-1,Height-1),Radius)) {
                using (var brush = new SolidBrush(BackColor)) e.Graphics.FillPath(brush,path);
                if (!LineColor.IsEmpty) using (var pen = new Pen(LineColor,1.5f)) e.Graphics.DrawPath(pen,path);
            }
            base.OnPaint(e);
        }
    }

    internal class DiscordButton : Button {
        bool hover;
        public int Radius = 7;
        public DiscordButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = Theme.Blurple; ForeColor = Theme.Text; Font = new Font("Segoe UI",10,FontStyle.Bold);
            Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e) {
            var g = e.Graphics; g.Clear(Parent == null ? Theme.Canvas : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Color.FromArgb(57,59,66) : hover ? ControlPaint.Light(BackColor,0.12f) : BackColor;
            using (var path = Theme.Round(new RectangleF(0,0,Width-1,Height-1),Radius))
            using (var brush = new SolidBrush(fill)) g.FillPath(brush,path);
            TextRenderer.DrawText(g,Text,Font,new Rectangle(4,0,Width-8,Height),Enabled ? ForeColor : Theme.Faint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) using (var path = Theme.Round(new RectangleF(3,3,Width-7,Height-7),Math.Max(2,Radius-2)))
                using (var pen = new Pen(Theme.Text)) g.DrawPath(pen,path);
        }
    }

    public class DarkFrame : Form {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);
        public DarkFrame(string title, Size size, bool minimize) {
            Text = title; ClientSize = size; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI",10);
            BackColor = Theme.Canvas; ForeColor = Theme.Text; DoubleBuffered = true; Icon = Theme.MakeIcon();
            var bar = new Panel { Bounds = new Rectangle(1,1,size.Width-2,34), BackColor = Theme.Rail };
            Controls.Add(bar);
            var caption = Theme.Label(bar,title.ToUpperInvariant(),14,7,size.Width-120,23,9,Theme.Faint,true);
            MouseEventHandler drag = delegate(object sender, MouseEventArgs e) {
                if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); }
            };
            bar.MouseDown += drag; caption.MouseDown += drag;
            var close = new DiscordButton { Text = "\u00d7", BackColor = Theme.Rail, ForeColor = Theme.Muted,
                Bounds = new Rectangle(size.Width-43,0,40,32), Radius = 3, AccessibleName = "Close window", TabStop = false, Font = new Font("Segoe UI",16) };
            close.Click += delegate { Close(); }; bar.Controls.Add(close);
            if (minimize) {
                var min = new DiscordButton { Text = "\u2013", BackColor = Theme.Rail, ForeColor = Theme.Muted,
                    Bounds = new Rectangle(size.Width-85,0,40,32), Radius = 3, AccessibleName = "Minimize window", TabStop = false };
                min.Click += delegate { WindowState = FormWindowState.Minimized; }; bar.Controls.Add(min);
            }
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); using (var pen = new Pen(Color.FromArgb(20,21,24))) e.Graphics.DrawRectangle(pen,0,0,ClientSize.Width-1,ClientSize.Height-1);
        }
    }

    public sealed class CleanerWindow : DarkFrame {
        readonly TextBox token, channel;
        readonly Label summary, status, recipient, count, countCaption, phase;
        readonly DiscordButton scan, delete, stop, help;
        readonly Button showToken;
        CancellationTokenSource cancel;
        DiscordApi api;
        Cleaner cleaner;
        Plan plan;
        bool busy;

        public CleanerWindow() : base("My DM Cleaner 1.0.0",new Size(896,632),true) {
            var top = new Panel { Bounds = new Rectangle(1,35,894,60), BackColor = Theme.Canvas }; Controls.Add(top);
            Theme.Label(top,"Direct Message Cleaner",31,18,660,28,13,Theme.Text,true);
            help = new DiscordButton { Text = "?", Radius = 17, BackColor = Theme.Card, ForeColor = Theme.Muted,
                Bounds = new Rectangle(823,12,40,36), AccessibleName = "Open the step-by-step setup guide" };
            help.Click += delegate { ShowHelp(); }; top.Controls.Add(help);
            Controls.Add(new Panel { BackColor = Theme.Rail, Bounds = new Rectangle(1,94,894,1) });

            var connection = new RoundPanel { BackColor = Theme.Card, Bounds = new Rectangle(32,118,510,354) }; Controls.Add(connection);
            Theme.Label(connection,"CONNECTION",20,19,300,23,9,Theme.Muted,true);
            Theme.Label(connection,"Account token",20,60,290,25,10,Theme.Text,true);
            var tokenPanel = new RoundPanel { BackColor = Theme.Input, Radius = 6, Bounds = new Rectangle(20,91,470,46) }; connection.Controls.Add(tokenPanel);
            token = Theme.Edit(tokenPanel,405,true); token.AccessibleName = "Account token"; token.TabIndex = 0;
            showToken = new DiscordButton { Text = "Show", BackColor = Theme.Input, ForeColor = Theme.Muted,
                Font = new Font("Segoe UI",9), Bounds = new Rectangle(404,7,58,32), AccessibleName = "Show or hide account token", TabIndex = 1 };
            showToken.Click += delegate { token.UseSystemPasswordChar = !token.UseSystemPasswordChar; showToken.Text = token.UseSystemPasswordChar ? "Show" : "Hide"; };
            tokenPanel.Controls.Add(showToken);
            Theme.Label(connection,"Direct message",20,184,430,25,10,Theme.Text,true);
            var linkPanel = new RoundPanel { BackColor = Theme.Input, Radius = 6, Bounds = new Rectangle(20,215,470,46) }; connection.Controls.Add(linkPanel);
            channel = Theme.Edit(linkPanel,470,false); channel.AccessibleName = "DM message link or channel ID"; channel.TabIndex = 0;
            Theme.Label(connection,"Paste a message link or the DM channel ID.",20,270,470,24,9,Theme.Faint);
            scan = new DiscordButton { Text = "Scan conversation", Bounds = new Rectangle(20,304,270,36), AccessibleName = "Scan conversation without deleting messages" };
            stop = new DiscordButton { Text = "Stop", BackColor = Color.FromArgb(78,80,88), Bounds = new Rectangle(302,304,188,36), Enabled = false };
            connection.Controls.AddRange(new Control[] {scan,stop});

            var preview = new RoundPanel { BackColor = Theme.Card, Bounds = new Rectangle(562,118,302,354) }; Controls.Add(preview);
            Theme.Label(preview,"CONVERSATION",20,19,260,23,9,Theme.Muted,true);
            recipient = Theme.Label(preview,"No DM selected",20,58,262,31,14,Theme.Text,true);
            count = Theme.Label(preview,"\u2014",18,92,264,65,36,Theme.Text,true);
            countCaption = Theme.Label(preview,"your messages ready to delete",20,160,262,25,9.5f,Theme.Muted);
            summary = Theme.Label(preview,"Run a scan to see the account, recipient, and message count before deleting.",20,197,262,102,9,Theme.Faint);
            delete = new DiscordButton { Text = "Delete my messages", BackColor = Theme.Danger, Bounds = new Rectangle(20,304,262,36), Enabled = false };
            preview.Controls.Add(delete);

            var activity = new RoundPanel { BackColor = Theme.Card, Bounds = new Rectangle(32,492,832,112) }; Controls.Add(activity);
            Theme.Label(activity,"ACTIVITY",20,14,500,22,8.5f,Theme.Muted,true);
            phase = Theme.Label(activity,"READY",580,14,232,22,8.5f,Theme.Faint,true); phase.TextAlign = ContentAlignment.TopRight;
            status = Theme.Label(activity,"Add your token and a DM link, then scan to preview your messages.",20,44,792,59,9.5f,Theme.Muted);

            scan.Click += async delegate { await Scan(); };
            delete.Click += async delegate { await Delete(); };
            stop.Click += delegate { if (cancel != null) { cancel.Cancel(); stop.Enabled = false; Phase("Stopping",Theme.Gold); status.Text = "Stopping. A request already sent may still finish."; } };
            token.TextChanged += delegate { InvalidatePlan(); };
            channel.TextChanged += delegate { InvalidatePlan(); };
            Shown += delegate { token.Focus(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { if (cancel != null) cancel.Cancel(); e.Cancel = true; Phase("Stopping",Theme.Gold); status.Text = "Stopping; close the window again when it finishes."; }
                else { token.Clear(); ClearClient(); }
            };
            Busy(false);
        }
        void Phase(string name, Color color) { phase.Text = name.ToUpperInvariant(); phase.ForeColor = color; }
        void InvalidatePlan() {
            if (!busy) { plan = null; recipient.Text = "No DM selected"; count.Text = "\u2014"; countCaption.Text = "your messages ready to delete";
                summary.Text = "Run a scan to see the account, recipient, and message count before deleting."; Phase("Ready",Theme.Faint); ClearClient(); Busy(false); }
        }
        void ClearClient() { if (api != null) { api.Dispose(); api = null; cleaner = null; } }
        void UpdateStatus(string text) { status.Text = text; }
        void Busy(bool value) {
            busy = value; token.Enabled = channel.Enabled = showToken.Enabled = help.Enabled = !value;
            scan.Enabled = !value && !String.IsNullOrWhiteSpace(token.Text) && !String.IsNullOrWhiteSpace(channel.Text);
            stop.Enabled = value; delete.Enabled = !value && plan != null && plan.MessageIds.Count > 0;
        }
        async Task Scan() {
            plan = null; ClearClient(); summary.Text = "Checking your account and the DM recipient..."; recipient.Text = "Finding conversation"; count.Text = "\u2014"; countCaption.Text = "scanning your available history";
            try {
                string id = Cleaner.ChannelId(channel.Text); api = new DiscordApi(token.Text); api.Status = UpdateStatus;
                cleaner = new Cleaner(api); cleaner.Status = UpdateStatus;
                cancel = new CancellationTokenSource(); Busy(true); Phase("Scanning",Theme.Blurple); status.Text = "Checking the account and one-to-one DM...";
                plan = await cleaner.Scan(id,cancel.Token);
                recipient.Text = "@" + plan.RecipientName; count.Text = plan.MessageIds.Count.ToString("N0"); countCaption.Text = "your messages ready to delete";
                summary.Text = "YOUR ACCOUNT\r\n@" + plan.AccountName + "\r\n" + plan.AccountId + "\r\nRECIPIENT ID  " + plan.RecipientId + "\r\n" + plan.SystemMessages + " system entries excluded";
                status.Text = "Scan complete. Review the account, recipient, and message count before deleting."; Phase(plan.MessageIds.Count > 0 ? "Ready to review" : "Nothing to delete",Theme.Green);
            } catch (OperationCanceledException) { status.Text = "Scan stopped. No messages deleted."; Phase("Stopped",Theme.Gold); }
            catch (Exception error) { status.Text = SafeMessage(error); Phase("Needs attention",Theme.Danger); }
            finally {
                if (cancel != null) { cancel.Dispose(); cancel = null; } Busy(false);
                if (plan == null) { ClearClient(); recipient.Text = "Scan incomplete"; countCaption.Text = "scan again to preview your messages"; summary.Text = "No messages were deleted during this scan."; }
            }
        }
        async Task Delete() {
            if (plan == null || plan.MessageIds.Count == 0) return;
            using (var dialog = new ConfirmDeleteDialog(plan)) if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var progress = new DeleteProgress();
            try {
                cancel = new CancellationTokenSource(); Busy(true); Phase("Deleting",Theme.Danger);
                status.Text = "Rechecking ownership before deleting each message...";
                await cleaner.Delete(plan,progress,cancel.Token);
                count.Text = progress.Deleted.ToString("N0"); countCaption.Text = "messages deleted";
                status.Text = "Finished. Deleted " + progress.Deleted + "; already gone " + progress.AlreadyGone + "; not found during verification " + progress.UnverifiedSkipped + "; skipped system entries " + progress.SystemSkipped + ". Scan again to verify."; Phase("Finished",Theme.Green);
            } catch (OperationCanceledException) { status.Text = "Stopped after " + progress.Deleted + " confirmed deletions. Scan again; a request in flight may have completed."; Phase("Stopped",Theme.Gold); }
            catch (Exception error) { status.Text = SafeMessage(error) + " Confirmed deleted: " + progress.Deleted + ". Scan again before retrying."; Phase("Needs attention",Theme.Danger); }
            finally { plan = null; if (cancel != null) { cancel.Dispose(); cancel = null; } Busy(false); ClearClient(); }
        }
        void ShowHelp() {
            using (var shade = new GuideShade { Bounds = Bounds })
            using (var dialog = new SetupGuideDialog(token.Text,channel.Text,value => token.Text = value,value => channel.Text = value)) {
                // WinForms refuses to show a disabled form, including a nonmodal shade.
                // Show it without activation first, then make it ignore input.
                shade.Show(this); shade.Enabled = false; dialog.Shade = shade;
                try { dialog.ShowDialog(this); } finally { shade.Close(); }
            }
        }
        static string SafeMessage(Exception error) {
            if (error is HttpRequestException) return "Could not reach Discord. Stopped. Check your connection and scan again.";
            if (error is ApiFailure || error is ArgumentException || error is InvalidOperationException) return error.Message;
            return "Unexpected error. Stopped. Scan again before retrying.";
        }
        [STAThread] static void Main() {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new CleanerWindow());
        }
    }

    internal sealed class GuideShade : Form {
        public GuideShade() { FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; BackColor = Color.Black; Opacity = 0; }
        protected override bool ShowWithoutActivation { get { return true; } }
    }

    internal sealed class SetupGuideDialog : DarkFrame {
        readonly RoundPanel content, tokenPanel, linkPanel;
        readonly TextBox tokenInput, linkInput;
        readonly Label stepLabel;
        readonly Panel[] marks = new Panel[5];
        readonly DiscordButton back, next;
        readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer { Interval = 15 };
        readonly Stopwatch clock = new Stopwatch();
        Point target, closingStart;
        double closingOpacity, closingShade;
        bool closing, allowClose;
        int page;
        internal Form Shade;
        internal bool TransitionRunning { get { return animation.Enabled; } }
        internal int Page { get { return page; } }
        public SetupGuideDialog(string initialToken = "",string initialLink = "",Action<string> onToken = null,Action<string> onLink = null)
            : base("SETUP GUIDE",new Size(640,590),false) {
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; Opacity = 0;
            Theme.Label(this,"Let's get you set up",28,58,440,40,22,Theme.Text,true);
            stepLabel = Theme.Label(this,"",480,69,132,26,10,Theme.Muted); stepLabel.TextAlign = ContentAlignment.TopRight;
            for (int i = 0; i < marks.Length; i++) { marks[i] = new Panel { Bounds = new Rectangle(28+i*119,113,108,3) }; Controls.Add(marks[i]); }
            content = new RoundPanel { BackColor = Theme.Card, Bounds = new Rectangle(28,138,584,360) }; Controls.Add(content);
            tokenPanel = new RoundPanel { BackColor = Theme.Input, Radius = 6, Bounds = new Rectangle(20,274,544,44) };
            tokenInput = Theme.Edit(tokenPanel,544,true); tokenInput.AccessibleName = "Paste your account token here"; tokenInput.Text = initialToken;
            tokenInput.TextChanged += delegate { if(onToken != null) onToken(tokenInput.Text); };
            linkPanel = new RoundPanel { BackColor = Theme.Input, Radius = 6, Bounds = new Rectangle(20,274,544,44) };
            linkInput = Theme.Edit(linkPanel,544,false); linkInput.AccessibleName = "Paste your DM message link here"; linkInput.Text = initialLink;
            linkInput.TextChanged += delegate { if(onLink != null) onLink(linkInput.Text); };
            back = new DiscordButton { Text = "Back", BackColor = Color.FromArgb(78,80,88), Bounds = new Rectangle(28,526,132,38) };
            next = new DiscordButton { Text = "Next", Bounds = new Rectangle(412,526,200,38) };
            back.Click += delegate { GoTo(page-1); };
            next.Click += delegate { if(page == 4) Close(); else GoTo(page+1); };
            Controls.AddRange(new Control[] {back,next}); AcceptButton = next;
            animation.Tick += delegate { Animate(); };
            GoTo(0);
        }
        internal void GoTo(int number) {
            if(number < 0 || number > 4 || closing) return;
            page = number; content.Controls.Remove(tokenPanel); content.Controls.Remove(linkPanel);
            while(content.Controls.Count > 0) content.Controls[0].Dispose();
            stepLabel.Text = "Step " + (page+1) + " of 5"; back.Enabled = page > 0; next.Text = page == 4 ? "Done" : "Next";
            for(int i = 0; i < marks.Length; i++) marks[i].BackColor = i <= page ? Theme.Blurple : Color.FromArgb(68,71,78);
            if(page == 0) {
                Heading("Open Discord in a browser");
                TextLine("Use Chrome or Edge for these steps.",72,30);
                Instruction("1. Open discord.com/app", "Sign in to your own Discord account.",120,54);
                var open = new DiscordButton { Text = "Open Discord in browser", Bounds = new Rectangle(20,183,260,36) };
                open.Click += delegate {
                    try { Process.Start(new ProcessStartInfo("https://discord.com/app") { UseShellExecute = true }); }
                    catch { TextLine("Open discord.com/app in your browser yourself.",325,28); }
                }; content.Controls.Add(open);
                Instruction("2. Open the browser tools", "Press Ctrl + Shift + I. A panel will open beside the page.",246,65);
                TextLine("You can keep using the Discord desktop app afterward.",326,24,9);
            } else if(page == 1) {
                Heading("Find a message request");
                Instruction("1. Choose Network", "At the top of the panel, click Network. If it is hidden, click the \u00bb button.",74,68);
                Instruction("2. Make the list fill up", "Open a different DM, then return to the one you want. You do not need to send a message.",157,72);
                Instruction("3. Filter the list", "In the Filter box, type messages. Click a row that starts with messages?limit=.",245,68);
                TextLine("No rows? Leave Network open and switch DMs again.",329,23,9);
            } else if(page == 2) {
                Heading("Copy your account token");
                Instruction("1. Open Headers", "Click Headers, then scroll down to Request Headers.",74,60);
                var example = new RoundPanel { BackColor = Theme.Input, Radius = 6, Bounds = new Rectangle(20,142,544,70) }; content.Controls.Add(example);
                Theme.Label(example,"REQUEST HEADERS",14,10,510,20,8,Theme.Faint,true);
                Theme.Label(example,"authorization",14,36,135,23,10,Theme.Muted);
                Theme.Label(example,"the long value to copy",162,36,368,23,10,Theme.Text,true);
                TextLine("Find authorization. Copy only the long value beside it, then paste it below. This is your private login key.",225,43);
                content.Controls.Add(tokenPanel);
                TextLine("Keep it private. If authorization is missing, try another messages row.",328,27,9);
            } else if(page == 3) {
                Heading("Choose your conversation");
                Instruction("1. Open the Discord desktop app", "Go to the DM you want to clean.",74,58);
                Instruction("2. Copy a message link", "Right-click a message in that DM and choose Copy Message Link.",149,66);
                Instruction("3. Paste that link below", "This selects the conversation to scan.",225,47);
                content.Controls.Add(linkPanel);
                TextLine("Use a message link, not a person's user ID.",329,23,9);
            } else {
                Heading("Scan, check, then delete");
                Instruction("1. Scan conversation", "This only counts messages. Nothing is deleted during a scan.",74,62);
                Instruction("2. Check the preview", "Confirm your account, the other person, and the message count.",150,62);
                Instruction("3. Delete my messages", "Type the confirmation phrase shown, then confirm deletion.",226,62);
                TextLine("Stop cancels the remaining queue. Messages already deleted cannot be restored.",307,43,9);
            }
        }
        void Heading(string text) { Theme.Label(content,text,20,20,544,42,18,Theme.Text,true); }
        void TextLine(string text,int y,int h,float size = 10) { Theme.Label(content,text,20,y,544,h,size,Theme.Muted); }
        void Instruction(string title,string body,int y,int height) {
            Theme.Label(content,title,20,y,544,24,10,Theme.Text,true);
            Theme.Label(content,body,20,y+27,544,height-22,10,Theme.Muted);
        }
        protected override void OnShown(EventArgs e) {
            base.OnShown(e); target = Location; Location = new Point(target.X,target.Y+12); clock.Restart(); animation.Start();
        }
        void Animate() {
            double t = Math.Min(1,clock.Elapsed.TotalMilliseconds / (closing ? 140.0 : 190.0));
            double ease = 1-Math.Pow(1-t,3);
            if(closing) {
                Opacity = Math.Max(0,closingOpacity*(1-ease));
                Location = new Point(closingStart.X,closingStart.Y+(int)Math.Round(8*ease));
                if(Shade != null && !Shade.IsDisposed) Shade.Opacity = closingShade*(1-ease);
                if(t >= 1) { animation.Stop(); allowClose = true; Close(); }
            } else {
                Opacity = ease; Location = new Point(target.X,target.Y+(int)Math.Round(12*(1-ease)));
                if(Shade != null && !Shade.IsDisposed) Shade.Opacity = 0.38*ease;
                if(t >= 1) animation.Stop();
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) {
            if(!allowClose && Visible && e.CloseReason == CloseReason.UserClosing) {
                e.Cancel = true;
                if(!closing) { closing = true; closingOpacity = Opacity; closingShade = Shade != null && !Shade.IsDisposed ? Shade.Opacity : 0; closingStart = Location; back.Enabled = next.Enabled = false; clock.Restart(); animation.Start(); }
            }
            base.OnFormClosing(e);
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData) {
            if(keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref message,keyData);
        }
        protected override void Dispose(bool disposing) {
            if(disposing) { animation.Dispose(); if(tokenPanel != null && !tokenPanel.IsDisposed) tokenPanel.Dispose(); if(linkPanel != null && !linkPanel.IsDisposed) linkPanel.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class ConfirmDeleteDialog : DarkFrame {
        public ConfirmDeleteDialog(Plan plan) : base("CONFIRM DELETION",new Size(580,510),false) {
            StartPosition = FormStartPosition.CenterParent;
            Theme.Label(this,"Delete your messages?",28,60,524,42,22,Theme.Text,true);
            Theme.Label(this,"This permanently removes " + plan.MessageIds.Count.ToString("N0") + " of your messages\r\nfrom your conversation with @" + plan.RecipientName + ".",30,114,520,58,11,Theme.Muted);
            var details = new RoundPanel { BackColor = Theme.Card, Bounds = new Rectangle(28,184,524,140) }; Controls.Add(details);
            Theme.Label(details,"YOUR ACCOUNT",16,12,490,20,8,Theme.Faint,true);
            Theme.Label(details,"@" + plan.AccountName,16,34,490,25,10,Theme.Text);
            Theme.Label(details,"Account: " + plan.AccountId + "\r\nRecipient: " + plan.RecipientId + "\r\nDM channel: " + plan.ChannelId,16,67,490,60,9,Theme.Muted);
            string expected = "DELETE " + plan.MessageIds.Count;
            Theme.Label(this,"Type " + expected + " to confirm",30,344,520,24,10,Theme.Text,true);
            var entry = new RoundPanel { BackColor = Theme.Input, Radius = 6, Bounds = new Rectangle(28,376,524,46) }; Controls.Add(entry);
            var input = Theme.Edit(entry,524,false); input.AccessibleName = "Deletion confirmation phrase";
            var no = new DiscordButton { Text = "Keep my messages", BackColor = Color.FromArgb(78,80,88), Bounds = new Rectangle(28,450,250,36), DialogResult = DialogResult.Cancel };
            var ok = new DiscordButton { Text = "Delete permanently", BackColor = Theme.Danger, Bounds = new Rectangle(290,450,262,36), Enabled = false, DialogResult = DialogResult.OK };
            input.TextChanged += delegate { ok.Enabled = input.Text == expected; };
            Controls.AddRange(new Control[] {no,ok}); CancelButton = no; AcceptButton = ok;
            Shown += delegate { input.Focus(); };
        }
    }
}
