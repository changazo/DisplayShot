using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DisplayShot
{
    // Toolbar under the selection (with the gear menu that shows/hides its items and controls the
    // background mode), and the Copy / Save as / Save / Close actions. All sizes follow the DPI of the
    // monitor the selection is on.
    internal sealed partial class OverlayForm
    {
        sealed class ToolbarItem
        {
            public string Id, Label, Glyph;
            public int Group, DesignWidth;   // width in 96-DPI pixels
            public Control Control;           // null for the size readout, which is painted
            public int X;                     // set by LayoutToolbar
            public bool Visible;
        }

        float uiScale = 1f;                    // DPI scale of the monitor the toolbar is on
        int S(float v) { return (int)Math.Round(v * uiScale); }

        BufferedPanel toolbar;
        ImageButton btnRect, btnOval, btnLine, btnArrow, btnBrush, btnText, btnPixelate, btnUndo, btnExit, btnCopy, btnSaveAs, btnGear;
        SaveButton btnQuickSave;   // floppy square + corner badge that changes the save folder
        readonly List<ToolbarItem> toolbarItems = new List<ToolbarItem>();
        readonly List<int> dividers = new List<int>();
        ToolbarItem readout;
        ContextMenuStrip gearMenu;
        ToolStripMenuItem miCopyOnSave, miBackground, miStartup;
        Point toolbarAnchor;

        void InitControls()
        {
            toolbar = new BufferedPanel();
            toolbar.BackColor = Theme.Background;
            toolbar.Visible = false;
            toolbar.Paint += PaintToolbarDecorations;

            // Groups: size | tools | undo close | copy, save as, save | gear
            readout = AddItem("size", "Size (W × H)", 0, null, null, 92);
            btnRect = AddButton("rect", "Rectangle", 1, "tool-rect", "Rectangle");
            btnOval = AddButton("oval", "Oval", 1, "tool-oval", "Oval");
            btnLine = AddButton("line", "Line", 1, "tool-line", "Line (hold Shift for 45° steps)");
            btnArrow = AddButton("arrow", "Arrow", 1, "tool-arrow", "Arrow (hold Shift for 45° steps)");
            btnBrush = AddButton("brush", "Brush", 1, "tool-brush", "Brush");
            btnText = AddButton("text", "Text", 1, "tool-text", "Text");
            btnPixelate = AddButton("pixelate", "Pixelate", 1, "tool-pixelate", "Pixelate (hide sensitive details)");
            btnUndo = AddButton("undo", "Undo", 2, "action-undo", "Undo (Ctrl+Z)   Redo (Ctrl+Y)");
            btnExit = AddButton("close", "Close", 2, "action-close", "Close (Esc)");
            btnCopy = AddButton("copy", "Copy", 3, "action-copy", "Copy to clipboard (Ctrl+C)");
            btnSaveAs = AddButton("saveas", "Save as", 3, "action-saveas", "Save as… choose folder and format (Ctrl+Shift+S)");
            btnSaveAs.Primary = true;
            btnQuickSave = new SaveButton();
            AddItem("save", "Save", 3, btnQuickSave, null, 39);

            btnGear = new ImageButton();
            btnGear.Hint = "Toolbar buttons and options";
            toolbar.Controls.Add(btnGear);
            InitGearMenu();

            btnRect.Click += delegate { SelectTool(Tool.Rectangle); };
            btnOval.Click += delegate { SelectTool(Tool.Oval); };
            btnLine.Click += delegate { SelectTool(Tool.Line); };
            btnArrow.Click += delegate { SelectTool(Tool.Arrow); };
            btnBrush.Click += delegate { SelectTool(Tool.Brush); };
            btnText.Click += delegate { SelectTool(Tool.Text); };
            btnPixelate.Click += delegate { SelectTool(Tool.Pixelate); };
            btnUndo.Click += delegate { Undo(); };
            btnExit.Click += delegate { Close(); };
            btnCopy.Click += delegate { Copy(); };
            btnSaveAs.Click += delegate { SaveAs(); };
            btnQuickSave.SaveClick += delegate { QuickSave(); };
            btnQuickSave.FolderClick += delegate { ChooseSaveFolder(); };   // the corner badge, or a right-click
            btnGear.Click += delegate
            {
                gearMenu.Font = Theme.UiAt(uiScale);
                gearMenu.Show(btnGear, new Point(btnGear.Width - gearMenu.Width, btnGear.Height + S(4)));
            };

            Controls.Add(toolbar);
            UpdateSaveHints();
            InitToolControls();
            LayoutToolbar();
        }

        ToolbarItem AddItem(string id, string label, int group, Control control, string glyph, int designWidth)
        {
            ToolbarItem item = new ToolbarItem { Id = id, Label = label, Group = group, Control = control, Glyph = glyph, DesignWidth = designWidth };
            toolbarItems.Add(item);
            if (control != null) toolbar.Controls.Add(control);
            return item;
        }

        ImageButton AddButton(string id, string label, int group, string glyph, string hint)
        {
            ImageButton b = new ImageButton();
            b.Hint = hint;
            AddItem(id, label, group, b, glyph, 32);
            return b;
        }

        HashSet<string> HiddenIds()
        {
            return new HashSet<string>(Settings.HiddenButtons.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>Places the visible items left to right at the current scale (dividers only between
        /// non-empty groups), puts the gear at the end and sizes the toolbar to fit.</summary>
        void LayoutToolbar(bool place = true)
        {
            HashSet<string> hidden = HiddenIds();
            dividers.Clear();
            toolbar.Height = S(48);
            int x = S(12), lastGroup = -1, top = S(8), size = S(32);
            foreach (ToolbarItem item in toolbarItems)
            {
                item.Visible = !hidden.Contains(item.Id);
                if (item.Control != null) item.Control.Visible = item.Visible;
                ImageButton b = item.Control as ImageButton;
                if (b != null)
                {
                    b.Size = new Size(size, size);
                    b.Glyph = Assets.Glyph(item.Glyph, size);
                }
                if (item.Control == btnQuickSave) btnQuickSave.Scale = uiScale;
                if (!item.Visible) continue;
                if (lastGroup >= 0 && item.Group != lastGroup)
                {
                    dividers.Add(x + S(4));
                    x += S(17);
                }
                else if (lastGroup >= 0) x += S(8);
                item.X = x;
                if (item.Control != null) item.Control.Location = new Point(x, top);
                x += item.Control == btnQuickSave ? btnQuickSave.Width : S(item.DesignWidth);
                lastGroup = item.Group;
            }
            if (lastGroup >= 0)
            {
                dividers.Add(x + S(4));
                x += S(17);
            }
            btnGear.Size = new Size(size, size);
            btnGear.Glyph = Assets.Glyph("action-gear", size);
            btnGear.Location = new Point(x, top);
            toolbar.Width = x + size + S(10);
            if (place && toolbar.Visible) PlaceToolbar(toolbarAnchor);
            if (subPanel != null && subPanel.Visible) subPanel.Left = toolbar.Left;
            toolbar.Invalidate();
        }

        void InitGearMenu()
        {
            gearMenu = new ContextMenuStrip();
            gearMenu.Renderer = Theme.MenuRenderer();
            gearMenu.ShowImageMargin = false;
            gearMenu.ShowCheckMargin = true;
            ToolStripMenuItem title = new ToolStripMenuItem("Show in the toolbar");
            title.Enabled = false;
            gearMenu.Items.Add(title);
            gearMenu.Items.Add(new ToolStripSeparator());
            foreach (ToolbarItem item in toolbarItems)
            {
                ToolbarItem it = item;
                ToolStripMenuItem mi = new ToolStripMenuItem(it.Label);
                mi.Tag = it.Id;
                mi.Click += delegate { SetItemHidden(it.Id, it.Visible); };
                gearMenu.Items.Add(mi);
            }
            gearMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem showAll = new ToolStripMenuItem("Show all");
            showAll.Click += delegate { Settings.HiddenButtons = ""; LayoutToolbar(); SyncGearMenu(); };
            gearMenu.Items.Add(showAll);

            gearMenu.Items.Add(new ToolStripSeparator());
            miCopyOnSave = new ToolStripMenuItem("Also copy when saving");
            miCopyOnSave.Click += delegate
            {
                Settings.CopyOnSave = !Settings.CopyOnSave;
                miCopyOnSave.Checked = Settings.CopyOnSave;
                UpdateSaveHints();
            };
            gearMenu.Items.Add(miCopyOnSave);
            miBackground = new ToolStripMenuItem("Run in the background (Ctrl+Alt+X)");
            miBackground.Click += delegate
            {
                miBackground.Checked = !miBackground.Checked;   // a stopping resident may still be found for a moment
                Background.SetEnabled(miBackground.Checked);
            };
            miStartup = new ToolStripMenuItem("Start with Windows");
            miStartup.Click += delegate
            {
                Background.StartWithWindows = !miStartup.Checked;
                miStartup.Checked = Background.StartWithWindows;
                if (miStartup.Checked) miBackground.Checked = true;   // starting with Windows also starts it now
            };
            gearMenu.Items.Add(miBackground);
            gearMenu.Items.Add(miStartup);

            gearMenu.Opening += delegate { SyncGearMenu(); };
            // Stay open while items are being ticked; close on a click elsewhere.
            gearMenu.Closing += delegate(object o, ToolStripDropDownClosingEventArgs e)
            {
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
            };
        }

        void SetItemHidden(string id, bool hide)
        {
            HashSet<string> hidden = HiddenIds();
            if (hide) hidden.Add(id); else hidden.Remove(id);
            Settings.HiddenButtons = string.Join(",", new List<string>(hidden).ToArray());
            LayoutToolbar();
            SyncGearMenu();
        }

        void SyncGearMenu()
        {
            HashSet<string> hidden = HiddenIds();
            foreach (ToolStripItem mi in gearMenu.Items)
            {
                ToolStripMenuItem m = mi as ToolStripMenuItem;
                if (m != null && m.Tag is string) m.Checked = !hidden.Contains((string)m.Tag);
            }
            miCopyOnSave.Checked = Settings.CopyOnSave;
            miBackground.Checked = Background.IsEnabled;
            miStartup.Checked = Background.StartWithWindows;
        }

        /// <summary>Border, dividers and the "W × H" readout of the selection size.</summary>
        void PaintToolbarDecorations(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (Pen p = new Pen(Theme.Charcoal))
            {
                g.DrawRectangle(p, 0, 0, toolbar.Width - 1, toolbar.Height - 1);
                foreach (int x in dividers) g.DrawLine(p, x, S(10), x, S(38));
            }
            if (!readout.Visible) return;
            Font font = Theme.UiAt(uiScale);
            int rx = readout.X + S(1), h = toolbar.Height;
            TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, frontRect.Width.ToString(), font, new Rectangle(rx, 0, S(40), h), Theme.Lavender, f);
            TextRenderer.DrawText(g, "×", font, new Rectangle(rx + S(35), 0, S(12), h), Theme.Muted, f);
            TextRenderer.DrawText(g, frontRect.Height.ToString(), font, new Rectangle(rx + S(52), 0, S(40), h), Theme.Lavender, f);
        }

        /// <summary>Right-aligns the toolbar to P.X and puts it below the selection, above it, or inside it,
        /// whichever fits on the monitor of the selection's bottom-right corner. Rescales the toolbar
        /// when that monitor has a different DPI.</summary>
        void PlaceToolbar(Point p)
        {
            toolbarAnchor = p;
            float scale = Native.MonitorScale(new Point(p.X + desktop.X, p.Y + desktop.Y));
            if (Math.Abs(scale - uiScale) > 0.01f)
            {
                uiScale = scale;
                LayoutToolbar(false);
                LayoutSubPanel();
                Invalidate(Rectangle.Inflate(frontRect, 12, 12));   // handle size follows the scale
            }
            Rectangle mon = MonitorClientBounds(p);
            int left = Math.Max(Math.Max(mon.Left, 0), p.X - toolbar.Width);
            int top;
            if (mon.Bottom >= p.Y + toolbar.Height) top = p.Y;                         // below the selection
            else
            {
                int t0 = p.Y - frontRect.Height;                                          // = selection top - 1
                top = t0 - toolbar.Height > mon.Top ? t0 - toolbar.Height : t0 + S(8);   // above, else inside
            }
            toolbar.Location = new Point(left, top);
            toolbar.Invalidate();
        }

        // ------------------------------------------------------------------------------------------
        // Output

        /// <summary>The selection with its annotations.</summary>
        Bitmap FinalImage()
        {
            if (front == null) ReplaceFront();
            return front.ToBitmap();
        }

        void Copy()
        {
            CommitText();
            using (Bitmap bmp = FinalImage())
            {
                if (!PutOnClipboard(bmp))
                {
                    ShowWarning("The clipboard is busy. Please try again.");
                    return;
                }
            }
            Hide();
            if (!Settings.CopyMessageHidden)
                using (CopiedForm msg = new CopiedForm()) msg.ShowDialog();
            Close();
        }

        static bool PutOnClipboard(Bitmap bmp)
        {
            for (int i = 0; i < 20; i++)   // another app may hold the clipboard for a moment
            {
                try { Clipboard.SetImage(bmp); return true; }
                catch (ExternalException) { Thread.Sleep(100); }
            }
            return false;
        }

        /// <summary>After Save / Save as, with "Also copy when saving" on. The file is what matters, so a busy
        /// clipboard doesn't hold up the save.</summary>
        static void CopyAfterSave(Bitmap bmp)
        {
            if (Settings.CopyOnSave) PutOnClipboard(bmp);
        }

        /// <summary>Where Save writes: the folder chosen with Save's corner badge, or Pictures\Screenshots
        /// (the folder Windows itself uses for Win+PrtScn) until then.</summary>
        static string QuickSaveFolder()
        {
            string folder = Settings.SaveFolder;
            if (!string.IsNullOrEmpty(folder)) return folder;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }

        /// <summary>Save (floppy, Enter, Ctrl+S): PNG straight into the save folder, no dialog.</summary>
        void QuickSave()
        {
            CommitText();
            string folder = QuickSaveFolder();
            try
            {
                Directory.CreateDirectory(folder);
                string path = UniquePath(folder, DefaultFileName(), ".png");
                using (Bitmap bmp = FinalImage())
                {
                    SaveImage(bmp, path);
                    CopyAfterSave(bmp);
                }
                Close();
            }
            catch (Exception ex)
            {
                ShowWarning("The screenshot couldn't be saved to\n" + folder + "\n\n" + ex.Message +
                            "\n\nClick the small folder badge on the Save button to choose another folder.");
            }
        }

        /// <summary>Save as (folder button, Ctrl+Shift+S): the Explorer dialog, any folder and format.</summary>
        void SaveAs()
        {
            CommitText();
            SetToolButtonStates(Tool.None);
            TopMost = false;   // let the dialog appear above the overlay
            string error = null;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg;*.jpeg)|*.jpg;*.jpeg|Bitmap (*.bmp)|*.bmp|GIF image (*.gif)|*.gif|TIFF image (*.tif;*.tiff)|*.tif;*.tiff";
                dlg.FilterIndex = 1;
                dlg.DefaultExt = "png";
                dlg.AddExtension = true;
                dlg.OverwritePrompt = true;
                string folder = QuickSaveFolder();
                dlg.InitialDirectory = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                dlg.FileName = DefaultFileName();
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        using (Bitmap bmp = FinalImage())
                        {
                            SaveImage(bmp, dlg.FileName);
                            CopyAfterSave(bmp);
                        }
                        Close();
                        return;
                    }
                    catch (Exception ex) { error = ex.Message; }
                }
            }
            TopMost = true;
            SetToolButtonStates(tool);
            Activate();
            if (error != null) ShowWarning("The screenshot couldn't be saved:\n" + error);
        }

        void UpdateSaveHints()
        {
            btnQuickSave.MainHint = "Save to " + QuickSaveFolder() + (Settings.CopyOnSave ? " and copy" : "") + " (Enter or Ctrl+S)";
            btnQuickSave.BadgeHint = "Change the folder Save uses\n(now: " + QuickSaveFolder() + ")";
        }

        /// <summary>Corner badge / right-click on Save: pick the folder Save writes to.</summary>
        void ChooseSaveFolder()
        {
            TopMost = false;
            string folder = FolderPicker.Pick(this, "Folder for Save", QuickSaveFolder());
            TopMost = true;
            Activate();
            if (folder == null) return;
            Settings.SaveFolder = folder;
            UpdateSaveHints();
        }

        static string DefaultFileName() { return "DisplayShot " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss"); }

        /// <summary>folder\name.ext, or folder\name (2).ext etc. if that file already exists.</summary>
        static string UniquePath(string folder, string name, string ext)
        {
            string path = Path.Combine(folder, name + ext);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, name + " (" + i + ")" + ext);
            return path;
        }

        void ShowWarning(string text)
        {
            TopMost = false;
            MessageBox.Show(this, text, "DisplayShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            TopMost = true;
            Activate();
        }

        /// <summary>Writes the image in the format given by the file extension (PNG when unknown).</summary>
        static void SaveImage(Bitmap src, string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".bmp") { src.Save(path, ImageFormat.Bmp); return; }
            ImageFormat fmt = ext == ".jpg" || ext == ".jpeg" ? ImageFormat.Jpeg
                            : ext == ".gif" ? ImageFormat.Gif
                            : ext == ".tif" || ext == ".tiff" ? ImageFormat.Tiff
                            : ImageFormat.Png;
            using (Bitmap argb = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(argb))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
                }
                argb.Save(path, fmt);
            }
        }
    }

    /// <summary>Double-buffered panel.</summary>
    internal class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }
    }
}
