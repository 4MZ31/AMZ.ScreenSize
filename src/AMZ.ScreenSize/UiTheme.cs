using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AMZ.ScreenSize
{
    // Native controls keep their keyboard, focus and accessibility behavior.
    public static class UiTheme
    {
        public static bool Dark = true;
        public static Color Canvas { get { return Hex(Dark ? 0x131517 : 0xF5F6F7); } }
        public static Color Sidebar { get { return Hex(Dark ? 0x0E1012 : 0xECEFF1); } }
        public static Color Surface { get { return Hex(Dark ? 0x1A1D20 : 0xFFFFFF); } }
        public static Color Raised { get { return Hex(Dark ? 0x252A2F : 0xE9EDF0); } }
        public static Color Border { get { return Hex(Dark ? 0x353C43 : 0xCAD1D7); } }
        public static Color Text { get { return Hex(Dark ? 0xF0F1F2 : 0x1B2227); } }
        public static Color Secondary { get { return Hex(Dark ? 0xB1B7BE : 0x4C5962); } }
        public static Color Muted { get { return Hex(Dark ? 0x969DA6 : 0x626F79); } }
        public static Color Accent { get { return Hex(Dark ? 0x73C8B1 : 0x236E5B); } }
        public static Color Selection { get { return Hex(Dark ? 0x233A34 : 0xE0EEE8); } }
        public static Color Danger { get { return Hex(Dark ? 0xF1A39B : 0xA62D28); } }
        public static Color Hex(int value) { return Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255); }
        public static Font Body(float size = 10.5f) { return new Font("Segoe UI", size, FontStyle.Regular); }
        public static Font Medium(float size = 10.5f) { return new Font("Segoe UI Semibold", size, FontStyle.Regular); }
        public static Label Label(string text, float size = 10.5f, string role = null)
        {
            return new Label { Text = text, Font = Body(size), Dock = DockStyle.Fill, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, Tag = role, Margin = new Padding(0), UseMnemonic = false };
        }
        public static TableLayoutPanel Table(int columns, params int[] rows)
        {
            TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows.Length, Margin = new Padding(0) };
            foreach (int row in rows) panel.RowStyles.Add(new RowStyle(row == 0 ? SizeType.Percent : SizeType.Absolute, row == 0 ? 100 : row));
            return panel;
        }
        public static UiButton Button(string text, bool primary = false)
        { return new UiButton { Text = text, Primary = primary, Height = 40, Width = 160, Margin = new Padding(0), Cursor = Cursors.Hand }; }
        public static ComboBox Formats(string accessibleName)
        {
            ComboBox combo = new UiComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 28, AccessibleName = accessibleName, Margin = new Padding(0, 4, 0, 4) };
            foreach (ScreenPreset preset in ScreenPreset.All) combo.Items.Add(preset);
            combo.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (Brush brush = new SolidBrush(selected ? Selection : Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, combo.Items[e.Index].ToString(), combo.Font, Rectangle.Inflate(e.Bounds, -8, 0), selected ? Accent : Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            return combo;
        }
        public static Control Input(TextBox input)
        {
            Panel shell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 8), Margin = new Padding(0, 0, 0, 4), Tag = "input" };
            input.BorderStyle = BorderStyle.None; input.Dock = DockStyle.Fill; shell.Controls.Add(input);
            shell.Paint += delegate(object s, PaintEventArgs e) { using (Pen pen = new Pen(input.Focused ? Accent : Border)) e.Graphics.DrawRectangle(pen, 0, 0, shell.Width - 1, shell.Height - 1); };
            input.GotFocus += delegate { shell.Invalidate(); }; input.LostFocus += delegate { shell.Invalidate(); };
            return shell;
        }
        public static void Apply(Control root)
        {
            string role = root.Tag as string;
            root.BackColor = role == "sidebar" ? Sidebar : role == "surface" || role == "input" || root is TextBox || root is ComboBox || root is ListBox || root is ListView ? Surface : root.Parent == null ? Canvas : root.Parent.BackColor;
            root.ForeColor = role == "muted" ? Muted : role == "secondary" ? Secondary : role == "danger" ? Danger : Text;
            Form form = root as Form;
            if (form != null && form.IsHandleCreated) Caption(form.Handle);
            foreach (Control child in root.Controls) Apply(child);
            root.Invalidate(true);
        }
        public static void Caption(IntPtr handle)
        {
            try { int dark = Dark ? 1 : 0; DwmSetWindowAttribute(handle, 20, ref dark, 4); }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
        public static void PaintBackground(Control control, Graphics graphics)
        {
            Color color = control.Parent == null ? Canvas : control.Parent.BackColor;
            if (color.A != 255) color = Canvas;
            using (Brush brush = new SolidBrush(color)) graphics.FillRectangle(brush, control.ClientRectangle);
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        public static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
    }

    public sealed class UiComboBox : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if ((m.Msg != 0xF && m.Msg != 0x317 && m.Msg != 0x318) || !IsHandleCreated) return;
            using (Graphics g = m.Msg == 0xF ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(m.WParam))
            {
                float s = g.DpiX / 96;
                Rectangle arrow = new Rectangle(Width - (int)(28 * s), 0, (int)(28 * s), Height);
                using (Brush b = new SolidBrush(UiTheme.Surface)) g.FillRectangle(b, arrow);
                using (Pen p = new Pen(Focused ? UiTheme.Accent : UiTheme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                using (Pen p = new Pen(Enabled ? UiTheme.Secondary : UiTheme.Muted, 1.4f * s))
                {
                    int x = arrow.X + arrow.Width / 2, y = Height / 2;
                    g.DrawLines(p, new[] { new Point(x - (int)(4 * s), y - (int)(2 * s)), new Point(x, y + (int)(2 * s)), new Point(x + (int)(4 * s), y - (int)(2 * s)) });
                }
            }
        }
    }

    public class UiButton : Button
    {
        public bool Primary, Selected, Quiet, Destructive;
        public string Symbol;
        private bool hover, pressed;
        public UiButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; Font = UiTheme.Medium();
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, false);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { UiTheme.PaintBackground(this, e.Graphics); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintBackground(this, e.Graphics);
            float scale = e.Graphics.DpiX / 96f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color background = Selected ? UiTheme.Selection : Primary && Enabled ? UiTheme.Text : hover && Enabled ? UiTheme.Raised : Quiet ? BackColor : UiTheme.Surface;
            if (pressed && Enabled) background = UiTheme.Border;
            Color ink = !Enabled ? UiTheme.Muted : Selected ? UiTheme.Accent : Destructive ? UiTheme.Danger : Primary ? UiTheme.Canvas : UiTheme.Text;
            using (GraphicsPath path = UiTheme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 8 * scale))
            {
                using (Brush brush = new SolidBrush(background)) e.Graphics.FillPath(brush, path);
                if (!Primary && !Quiet && !Selected) using (Pen pen = new Pen(UiTheme.Border)) e.Graphics.DrawPath(pen, path);
                if (Focused && ShowFocusCues) using (Pen pen = new Pen(UiTheme.Accent, 2 * scale)) e.Graphics.DrawPath(pen, path);
            }
            Rectangle text = ClientRectangle;
            if (Symbol != null)
            {
                UiIcon.Draw(e.Graphics, Symbol, new RectangleF(16 * scale, (Height - 18 * scale) / 2, 18 * scale, 18 * scale), ink);
                text.X = (int)(46 * scale); text.Width -= text.X + (int)(12 * scale);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, text, ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                (Symbol == null ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
        }
    }
    public sealed class UiSwitch : CheckBox
    {
        public UiSwitch()
        {
            Height = 40; Font = UiTheme.Body(); Cursor = Cursors.Hand; AutoSize = false; UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, false);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { UiTheme.PaintBackground(this, e.Graphics); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            UiTheme.PaintBackground(this, e.Graphics);
            float s = e.Graphics.DpiX / 96; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF track = new RectangleF(Width - 44 * s, (Height - 24 * s) / 2, 40 * s, 24 * s);
            using (GraphicsPath path = UiTheme.Rounded(track, 12 * s)) using (Brush brush = new SolidBrush(Checked && Enabled ? UiTheme.Accent : UiTheme.Border)) e.Graphics.FillPath(brush, path);
            using (Brush brush = new SolidBrush(Checked ? UiTheme.Canvas : UiTheme.Secondary)) e.Graphics.FillEllipse(brush, track.X + (Checked ? 19 : 3) * s, track.Y + 3 * s, 18 * s, 18 * s);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width - (int)(56 * s), Height), Enabled ? UiTheme.Text : UiTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), UiTheme.Accent, BackColor);
        }
    }
    public sealed class UiMenuRenderer : ToolStripProfessionalRenderer
    {
        public UiMenuRenderer() : base(new UiMenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? UiTheme.Text : UiTheme.Muted; base.OnRenderItemText(e); }
    }
    public sealed class UiMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return UiTheme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return UiTheme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return UiTheme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return UiTheme.Surface; } }
        public override Color MenuItemSelected { get { return UiTheme.Selection; } }
        public override Color MenuItemBorder { get { return UiTheme.Selection; } }
        public override Color MenuBorder { get { return UiTheme.Border; } }
        public override Color CheckBackground { get { return UiTheme.Selection; } }
        public override Color CheckSelectedBackground { get { return UiTheme.Selection; } }
        public override Color CheckPressedBackground { get { return UiTheme.Selection; } }
        public override Color SeparatorDark { get { return UiTheme.Border; } }
        public override Color SeparatorLight { get { return UiTheme.Surface; } }
        public override Color MenuItemPressedGradientBegin { get { return UiTheme.Selection; } }
        public override Color MenuItemPressedGradientEnd { get { return UiTheme.Selection; } }
    }
    // One 18px / 1.5px line system, drawn locally without icon fonts or emoji.
    public static class UiIcon
    {
        public static void Draw(Graphics g, string kind, RectangleF box, Color color)
        {
            System.Drawing.Drawing2D.GraphicsState state = g.Save();
            g.TranslateTransform(box.X, box.Y); g.ScaleTransform(box.Width / 24, box.Height / 24);
            using (Pen p = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (kind == "screen") { g.DrawRectangle(p, 2, 4, 20, 14); g.DrawLine(p, 12, 18, 12, 22); g.DrawLine(p, 7, 22, 17, 22); }
                else if (kind == "profiles") { g.DrawRectangle(p, 3, 4, 18, 16); g.DrawLine(p, 3, 10, 21, 10); g.DrawLine(p, 9, 10, 9, 20); }
                else if (kind == "settings") { g.DrawLine(p, 3, 6, 21, 6); g.DrawLine(p, 3, 12, 21, 12); g.DrawLine(p, 3, 18, 21, 18); g.DrawEllipse(p, 7, 3, 6, 6); g.DrawEllipse(p, 13, 9, 6, 6); g.DrawEllipse(p, 5, 15, 6, 6); }
                else if (kind == "plus") { g.DrawLine(p, 12, 4, 12, 20); g.DrawLine(p, 4, 12, 20, 12); }
                else if (kind == "search") { g.DrawEllipse(p, 3, 3, 12, 12); g.DrawLine(p, 14, 14, 21, 21); }
                else { g.DrawLine(p, 4, 9, 4, 4); g.DrawLine(p, 4, 4, 9, 4); g.DrawLine(p, 15, 4, 20, 4); g.DrawLine(p, 20, 4, 20, 9); g.DrawLine(p, 20, 15, 20, 20); g.DrawLine(p, 20, 20, 15, 20); g.DrawLine(p, 9, 20, 4, 20); g.DrawLine(p, 4, 20, 4, 15); }
            }
            g.Restore(state);
        }
    }
}
