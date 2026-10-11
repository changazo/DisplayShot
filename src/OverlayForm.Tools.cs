using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DisplayShot
{
    // Annotation tools: rectangle, oval, line, arrow, brush, text, pixelate; pens, colours, font size;
    // undo and redo.
    internal sealed partial class OverlayForm
    {
        internal enum Tool { None = 6, Rectangle = 1, Oval = 2, Brush = 3, Text = 4, Line = 5, Arrow = 7, Pixelate = 8 }

        static readonly Color DefaultColor = Color.FromArgb(0xED, 0x1C, 0x24);   // the red swatch
        // Annotation colours, two rows of eight.
        static readonly int[] SwatchRgb =
        {
            0x7F7F7F, 0x880015, 0xED1C24, 0xFFF200, 0x22B14C, 0x00A2E8, 0x3F48CC, 0xA349A4,
            0xFFFFFF, 0xB97A57, 0xFFAEC9, 0xEFE4B0, 0xB5E61D, 0x99D9EA, 0x7092BE, 0xC8BFE7
        };
        static readonly int[] FontSizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 };
        static readonly int[] PenWidths = { 1, 4, 8 };
        static readonly int[] PixelBlocks = { 6, 10, 16 };   // pixelate block size in image pixels

        sealed class Record
        {
            public Tool Kind;
            public Point P1, P2;                       // shape corners / line ends (front-local)
            public Color Color;
            public int Width;                          // pen width, or block size for pixelate
            public List<Point[]> Segments;             // paintbrush segments
            public AnnotationTextBox TextBox;          // text
        }

        Tool tool = Tool.None;
        bool drawMode;             // mouse on the selection draws instead of moving it
        bool annotated;            // something has been drawn: the pristine copy exists
        int penWidth = 1;
        int pixelLevel = 1;        // index into PixelBlocks
        Color shapeColor = DefaultColor, textColor = DefaultColor;
        int fontSize = 8;
        bool textEditing;          // a text box is open
        bool textToolArmed;        // a second Text click only toggles the sub-panel
        bool textToolSelected;     // hovering committed text reopens it for editing
        readonly bool[] panelToggle = new bool[9];
        bool mouseDrawing;
        Point drawStart;
        DateTime lastRubberBand = DateTime.MinValue;

        GdiSurface pristineFront;  // front before the first annotation (undo replays on top of it)
        GdiSurface committedFront; // front after the last finished operation (rubber band restores it)
        readonly List<Record> records = new List<Record>();
        readonly List<Record> redoStack = new List<Record>();
        readonly List<AnnotationTextBox> textBoxes = new List<AnnotationTextBox>();
        AnnotationTextBox currentText;

        BufferedPanel subPanel;
        ImageButton[] penButtons;
        Panel colorPreview;
        readonly List<SwatchButton> swatches = new List<SwatchButton>();
        SizePicker fontCombo;

        AnnotationTextBox ActiveTextBox { get { return textEditing ? currentText : null; } }

        // ------------------------------------------------------------------------------------------
        // Sub-panel under the toolbar: pen sizes (or pixel block sizes), colour, swatches, font size

        void InitToolControls()
        {
            subPanel = new BufferedPanel();
            subPanel.BackColor = Theme.Background;
            subPanel.Paint += delegate(object o, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Charcoal)) e.Graphics.DrawRectangle(p, 0, 0, subPanel.Width - 1, subPanel.Height - 1);
            };
            subPanel.Visible = false;

            penButtons = new ImageButton[3];
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                ImageButton b = new ImageButton();
                b.Click += delegate
                {
                    if (tool == Tool.Pixelate) pixelLevel = idx; else penWidth = PenWidths[idx];
                    SyncPenButtons();
                };
                subPanel.Controls.Add(b);
                penButtons[i] = b;
            }

            colorPreview = new Panel();
            colorPreview.BackColor = DefaultColor;
            colorPreview.Paint += delegate(object o, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Slate)) e.Graphics.DrawRectangle(p, 0, 0, colorPreview.Width - 1, colorPreview.Height - 1);
            };
            subPanel.Controls.Add(colorPreview);

            for (int i = 0; i < 16; i++)
            {
                Color c = Color.FromArgb(SwatchRgb[i] >> 16, (SwatchRgb[i] >> 8) & 0xFF, SwatchRgb[i] & 0xFF);
                SwatchButton sw = new SwatchButton(c);
                sw.Click += delegate { PickColor(c); };
                subPanel.Controls.Add(sw);
                swatches.Add(sw);
            }

            fontCombo = new SizePicker(FontSizes);
            fontCombo.ValueChanged += delegate { ApplyFontSize(); };
            fontCombo.Visible = false;
            subPanel.Controls.Add(fontCombo);

            Controls.Add(subPanel);
            subPanel.BringToFront();   // above the toolbar (it overlaps the toolbar's bottom edge)
            LayoutSubPanel();
            SyncPenButtons();
        }

        /// <summary>Sizes and places the sub-panel's controls at the current scale. In pixelate mode only the
        /// three block sizes are shown.</summary>
        void LayoutSubPanel()
        {
            if (subPanel == null) return;
            bool pixel = tool == Tool.Pixelate;
            int[] penX = { 3, 29, 55 };
            string[] penGlyph = { "pen-1", "pen-2", "pen-3" };
            for (int i = 0; i < 3; i++)
            {
                penButtons[i].Bounds = new Rectangle(S(penX[i]), S(6), S(26), S(26));
                penButtons[i].Glyph = Assets.Glyph(penGlyph[i], S(28));
            }
            colorPreview.Bounds = new Rectangle(S(87), S(7), S(24), S(24));
            for (int i = 0; i < swatches.Count; i++)
                swatches[i].Bounds = new Rectangle(S(119 + 16 * (i % 8)), S(i < 8 ? 6 : 22), S(12), S(12));
            fontCombo.Bounds = new Rectangle(S(6), S(8), S(74), S(23));
            fontCombo.Font = Theme.UiAt(uiScale);
            colorPreview.Visible = !pixel;
            foreach (SwatchButton sw in swatches) sw.Visible = !pixel;
            subPanel.Size = new Size(S(pixel ? 86 : 250), S(39));
        }

        void SyncPenButtons()
        {
            bool pixel = tool == Tool.Pixelate;
            string[] pixelHints = { "Small blocks", "Medium blocks", "Large blocks" };
            for (int i = 0; i < 3; i++)
            {
                penButtons[i].Selected = pixel ? pixelLevel == i : penWidth == PenWidths[i];
                penButtons[i].Hint = pixel ? pixelHints[i] : PenWidths[i] + " px";
            }
        }

        /// <summary>Puts the sub-panel under the toolbar, moving both above the selection (or inside it) when
        /// there is no room below on the selection's monitor.</summary>
        void PlaceSubPanel()
        {
            Rectangle mon = MonitorClientBounds(new Point(frontRect.Right - 1, frontRect.Bottom - 1));
            if (mon.Bottom < frontRect.Bottom + S(85))
            {
                if (frontRect.Top - S(90) < mon.Top)
                {
                    toolbar.Top = frontRect.Top + S(5);
                    subPanel.Top = frontRect.Top + S(50);
                }
                else
                {
                    toolbar.Top = frontRect.Top - S(90);
                    subPanel.Top = frontRect.Top - S(45);
                }
            }
            else subPanel.Top = toolbar.Top + S(45);
            subPanel.Left = toolbar.Left;
        }

        // ------------------------------------------------------------------------------------------
        // Tool buttons

        void SelectTool(Tool t)
        {
            if (t == Tool.Text) { SelectTextTool(); return; }
            textToolSelected = false;
            drawMode = true;
            tool = t;
            SetToolButtonStates(t);
            fontCombo.Visible = false;
            LayoutSubPanel();
            SyncPenButtons();
            SetPreview(shapeColor);
            if (textEditing) CommitText();
            PlaceSubPanel();
            ShowPens(true);
            ToggleSubPanel(t);
            textToolArmed = false;
        }

        void SelectTextTool()
        {
            textToolSelected = true;
            if (textToolArmed)
            {
                subPanel.Visible = !subPanel.Visible;   // second click only toggles the sub-panel
                if (textEditing) CommitText();
                return;
            }
            textToolArmed = true;
            tool = Tool.Text;
            drawMode = true;
            SetToolButtonStates(Tool.Text);
            LayoutSubPanel();
            fontCombo.Visible = true;
            SetPreview(textColor);
            PlaceSubPanel();
            ShowPens(false);
            ToggleSubPanel(Tool.Text);
        }

        void ToggleSubPanel(Tool t)
        {
            int i = (int)t;
            subPanel.Visible = !panelToggle[i];
            bool now = !panelToggle[i];
            for (int k = 0; k < panelToggle.Length; k++) panelToggle[k] = false;
            panelToggle[i] = now;
            if (subPanel.Visible) subPanel.BringToFront();
        }

        void ShowPens(bool show)
        {
            foreach (ImageButton b in penButtons) b.Visible = show;
        }

        void SetToolButtonStates(Tool t)
        {
            btnRect.Selected = t == Tool.Rectangle;
            btnOval.Selected = t == Tool.Oval;
            btnLine.Selected = t == Tool.Line;
            btnArrow.Selected = t == Tool.Arrow;
            btnBrush.Selected = t == Tool.Brush;
            btnText.Selected = t == Tool.Text;
            btnPixelate.Selected = t == Tool.Pixelate;
        }

        void SetPreview(Color c) { colorPreview.BackColor = c; }

        void PickColor(Color c)
        {
            shapeColor = c;
            textColor = c;
            if (currentText != null)
            {
                // The colour applies to all the text of the current box, and the size list is re-applied.
                currentText.SelectAll();
                currentText.SelectionColor = c;
                currentText.SelectionFont = new Font(currentText.Font.FontFamily, CurrentComboSize());
                currentText.Select(currentText.TextLength, 0);
                currentText.SelectionColor = c;
                currentText.SelectionFont = new Font(currentText.Font.FontFamily, CurrentComboSize());
                fontSize = CurrentComboSize();
                if (currentText.Visible) currentText.Focus();
            }
            SetPreview(c);
        }

        int CurrentComboSize() { return fontCombo.Value; }

        void ApplyFontSize()
        {
            fontSize = CurrentComboSize();
            if (currentText == null) return;
            currentText.SelectionFont = new Font(currentText.Font.FontFamily, fontSize);
            currentText.SelectionColor = textColor;
            if (currentText.Visible) currentText.Focus();
        }

        // ------------------------------------------------------------------------------------------
        // Mouse on the selection while a tool is active

        Point FrontLocal(Point p) { return new Point(p.X - frontRect.X, p.Y - frontRect.Y); }

        bool ToolsMouseDown(MouseEventArgs e)
        {
            if (mode != Mode.Selected) return false;
            if (!subPanel.Visible && !annotated) drawMode = false;   // panel closed before drawing: back to moving
            if (drawMode && !annotated)
            {
                annotated = true;
                pristineFront = GdiSurface.Crop(front, new Rectangle(0, 0, front.Width, front.Height));
                committedFront = GdiSurface.Crop(front, new Rectangle(0, 0, front.Width, front.Height));
            }
            Point local = FrontLocal(e.Location);
            if (tool == Tool.Text)
            {
                if (!textEditing) OpenTextBox(local);
                else CommitText();
                return true;
            }
            if (!drawMode) return false;
            drawStart = local;
            mouseDrawing = true;
            if (tool == Tool.Brush)
                AddRecord(new Record { Kind = Tool.Brush, Color = shapeColor, Width = penWidth, Segments = new List<Point[]>() });
            return true;
        }

        bool ToolsMouseMove(MouseEventArgs e)
        {
            if (mode != Mode.Selected || !drawMode) return false;
            if (!mouseDrawing && handlesEnabled)
            {
                int h = HandleAt(e.Location);   // the handles still resize the selection while a tool is active
                if (h != 0)
                {
                    if (Cursor != HandleCursor(h)) Cursor = HandleCursor(h);
                    return true;
                }
            }
            if (Cursor != Cursors.Default) Cursor = Cursors.Default;
            Point local = FrontLocal(e.Location);
            if (mouseDrawing) DrawShape(drawStart, local, true);
            else TextHoverReEdit(e.Location);
            return true;
        }

        bool ToolsMouseUp(MouseEventArgs e)
        {
            if (mode != Mode.Selected) return false;
            bool wasDrawing = mouseDrawing;
            mouseDrawing = false;
            if (drawMode && wasDrawing)
            {
                Point local = FrontLocal(e.Location);
                Point end = DrawShape(drawStart, local, false);
                if (!textEditing) SnapshotCommitted();
                if (IsRubberBandTool(tool))
                    AddRecord(new Record
                    {
                        Kind = tool, P1 = drawStart, P2 = end, Color = shapeColor,
                        Width = tool == Tool.Pixelate ? PixelBlocks[pixelLevel] : penWidth
                    });
            }
            return tool == Tool.Text || drawMode;
        }

        static bool IsRubberBandTool(Tool t)
        {
            return t == Tool.Rectangle || t == Tool.Oval || t == Tool.Line || t == Tool.Arrow || t == Tool.Pixelate;
        }

        /// <summary>Rubber-banded shapes (restored from the committed copy, at most every 10 ms), or one
        /// paintbrush segment drawn straight onto the image. Returns the end point used.</summary>
        Point DrawShape(Point a, Point b, bool live)
        {
            if (front == null) return b;
            IntPtr hdc = front.Hdc;
            if (IsRubberBandTool(tool))
            {
                if ((tool == Tool.Line || tool == Tool.Arrow) && (ModifierKeys & Keys.Shift) != 0) b = SnapTo45(a, b);
                b = ClampEndPoint(b);
                if (live && (DateTime.Now - lastRubberBand).TotalMilliseconds <= 10) return b;
                if (live) lastRubberBand = DateTime.Now;
                committedFront.CopyTo(hdc, 0, 0, front.Width, front.Height, 0, 0);
                if (tool == Tool.Pixelate) Pixelate(front, NormalizedRect(a, b), PixelBlocks[pixelLevel]);
                else if (tool == Tool.Arrow) DrawArrow(hdc, a, b, penWidth, shapeColor);
                else Gdi.WithPen(hdc, penWidth, shapeColor, delegate
                {
                    if (tool == Tool.Rectangle) Gdi.Rectangle(hdc, a.X, a.Y, b.X, b.Y);
                    else if (tool == Tool.Oval) Gdi.Ellipse(hdc, a.X, a.Y, b.X, b.Y);
                    else Gdi.Line(hdc, a.X, a.Y, b.X, b.Y);
                });
            }
            else if (tool == Tool.Brush)
            {
                Gdi.WithPen(hdc, penWidth, shapeColor, delegate { Gdi.Line(hdc, a.X, a.Y, b.X, b.Y); });
                drawStart = b;
                if (records.Count > 0 && records[records.Count - 1].Segments != null)
                    records[records.Count - 1].Segments.Add(new[] { a, b });
            }
            else return b;
            Invalidate(frontRect);
            return b;
        }

        static Rectangle NormalizedRect(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        /// <summary>A straight line ending in a filled arrowhead; the head grows with the pen width.</summary>
        static void DrawArrow(IntPtr hdc, Point a, Point b, int width, Color color)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            double ux = dx / len, uy = dy / len;
            double head = Math.Min(len, 10 + 3.5 * width), half = head * 0.5;
            double bx = b.X - ux * head, by = b.Y - uy * head;   // centre of the arrowhead's base
            Gdi.WithPen(hdc, width, color, delegate
            {
                Gdi.Line(hdc, a.X, a.Y, (int)Math.Round(bx + ux), (int)Math.Round(by + uy));
            });
            Point[] tri =
            {
                b,
                new Point((int)Math.Round(bx - uy * half), (int)Math.Round(by + ux * half)),
                new Point((int)Math.Round(bx + uy * half), (int)Math.Round(by - ux * half))
            };
            Gdi.FillPolygon(hdc, tri, color);
        }

        /// <summary>Replaces the area with flat blocks of its average colours (downscale with HALFTONE,
        /// upscale with nearest neighbour).</summary>
        static void Pixelate(GdiSurface surface, Rectangle r, int block)
        {
            r.Intersect(new Rectangle(0, 0, surface.Width, surface.Height));
            if (r.Width <= 0 || r.Height <= 0) return;
            int sw = (r.Width + block - 1) / block, sh = (r.Height + block - 1) / block;
            using (GdiSurface small = new GdiSurface(sw, sh))
            {
                Gdi.SetStretchBltMode(small.Hdc, Gdi.HALFTONE);
                Gdi.SetBrushOrgEx(small.Hdc, 0, 0, IntPtr.Zero);
                Gdi.StretchBlt(small.Hdc, 0, 0, sw, sh, surface.Hdc, r.X, r.Y, sw * block, sh * block, Native.SRCCOPY);
                int save = Gdi.SaveDC(surface.Hdc);
                Gdi.IntersectClipRect(surface.Hdc, r.Left, r.Top, r.Right, r.Bottom);
                Gdi.SetStretchBltMode(surface.Hdc, Gdi.COLORONCOLOR);
                Gdi.StretchBlt(surface.Hdc, r.X, r.Y, sw * block, sh * block, small.Hdc, 0, 0, sw, sh, Native.SRCCOPY);
                Gdi.RestoreDC(surface.Hdc, save);
            }
        }

        /// <summary>Shift + line/arrow: snaps the end point to the nearest multiple of 45 degrees.</summary>
        static Point SnapTo45(Point a, Point b)
        {
            int dx = b.X - a.X, dy = b.Y - a.Y;
            double angle = Math.Atan2(dy, dx);
            double step = Math.PI / 4;
            double snapped = Math.Round(angle / step) * step;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int sx = (int)Math.Round(Math.Cos(snapped)), sy = (int)Math.Round(Math.Sin(snapped));
            if (sx != 0 && sy != 0)
            {
                int d = (int)Math.Round(len / Math.Sqrt(2));   // diagonal: equal X and Y distances
                return new Point(a.X + sx * d, a.Y + sy * d);
            }
            return new Point(a.X + sx * (int)Math.Round(len), a.Y + sy * (int)Math.Round(len));
        }

        /// <summary>Keeps the dragged corner inside the selection.</summary>
        Point ClampEndPoint(Point p)
        {
            int m = tool == Tool.Pixelate ? 0 : penWidth;
            if (p.X < 0) p.X = m;
            if (p.Y < 0) p.Y = m;
            if (p.X > frontRect.Width) p.X = frontRect.Width - m + (m > 0 ? 1 : 0);
            if (p.Y > frontRect.Height) p.Y = frontRect.Height - m + (m > 0 ? 1 : 0);
            return p;
        }

        void SnapshotCommitted()
        {
            if (committedFront != null) committedFront.Dispose();
            committedFront = GdiSurface.Crop(front, new Rectangle(0, 0, front.Width, front.Height));
        }

        // ------------------------------------------------------------------------------------------
        // Text tool

        void OpenTextBox(Point local)
        {
            AnnotationTextBox box = new AnnotationTextBox();
            box.Font = new Font("Segoe UI", 8f);
            box.Size = new Size(fontSize * 2 + 50, fontSize * 2 + 8);
            int left = frontRect.Left + local.X, top = frontRect.Top + local.Y;
            if (left + box.Width > frontRect.Right) left = frontRect.Right - box.Width - 3;
            if (top + box.Height > frontRect.Bottom) top = frontRect.Bottom - box.Height - 3;
            box.Location = new Point(left, top);
            box.PaintBackground = PaintUnderTextBox;
            box.DragBounds = delegate { return frontRect; };
            box.ContentsResized += TextBoxContentsResized;
            Controls.Add(box);
            box.BringToFront();
            toolbar.BringToFront();
            subPanel.BringToFront();
            box.SelectionFont = new Font(box.Font.FontFamily, fontSize);
            box.SelectionColor = textColor;
            textBoxes.Add(box);
            currentText = box;
            textEditing = true;
            box.Focus();
        }

        /// <summary>Background of a text box: the current image under it.</summary>
        void PaintUnderTextBox(IntPtr hdc, Rectangle boxBounds)
        {
            if (front == null) return;
            front.CopyTo(hdc, 0, 0, boxBounds.Width, boxBounds.Height, boxBounds.X - frontRect.X, boxBounds.Y - frontRect.Y);
        }

        /// <summary>Grows the box with its text (width + 20, height + 22), wrapping once
        /// it reaches the selection's right edge, never past the selection.</summary>
        void TextBoxContentsResized(object sender, ContentsResizedEventArgs e)
        {
            AnnotationTextBox box = (AnnotationTextBox)sender;
            int availW = frontRect.Right - box.Left, availH = frontRect.Bottom - box.Top;
            int w = e.NewRectangle.Width + 20;
            if (w > availW) { box.WordWrap = true; w = availW; }
            int h = Math.Min(e.NewRectangle.Height + 22, availH);
            if (box.Width != w || box.Height != h) box.Size = new Size(Math.Max(w, box.Width), Math.Max(h, 10));
            box.Invalidate();
        }

        /// <summary>Burns the open text box into the image and records it for undo.</summary>
        void CommitText()
        {
            if (!textEditing || currentText == null) { textEditing = false; return; }
            AnnotationTextBox box = currentText;
            box.ReEditing = false;
            BurnText(box);
            box.Visible = false;
            AddRecord(new Record { Kind = Tool.Text, TextBox = box });
            textEditing = false;
            textToolArmed = false;
            if (annotated) SnapshotCommitted();
            Invalidate(frontRect);
            Focus();
        }

        void BurnText(AnnotationTextBox box)
        {
            Rectangle r = box.Bounds;
            using (GdiSurface buf = new GdiSurface(r.Width, r.Height))
            {
                box.RenderTo(buf.Hdc);
                buf.CopyTo(front.Hdc, r.X - frontRect.X, r.Y - frontRect.Y, r.Width, r.Height, 0, 0);
            }
        }

        /// <summary>With the Text tool chosen, hovering committed text reopens it for editing.</summary>
        void TextHoverReEdit(Point clientPoint)
        {
            if (!textToolSelected || pristineFront == null) return;
            for (int i = 0; i < records.Count; i++)
            {
                AnnotationTextBox box = records[i].TextBox;
                if (box == null) continue;
                Rectangle b = box.Bounds;
                if (!(b.Left < clientPoint.X && clientPoint.X < b.Right && b.Top < clientPoint.Y && clientPoint.Y < b.Bottom)) continue;
                if (box.ReEditing) return;
                if (textEditing && currentText != box) CommitText();
                records.RemoveAll(r => r.TextBox == box);
                Replay();
                SnapshotCommitted();
                box.ReEditing = true;
                box.Visible = true;
                box.BringToFront();
                toolbar.BringToFront();
                subPanel.BringToFront();
                currentText = box;
                tool = Tool.Text;
                textEditing = true;
                textToolArmed = true;
                box.Select(0, 0);
                box.Focus();
                box.Invalidate();
                return;
            }
        }

        // ------------------------------------------------------------------------------------------
        // Undo / redo

        /// <summary>A new drawing: recorded for undo; anything undone before can no longer be redone.</summary>
        void AddRecord(Record r)
        {
            records.Add(r);
            redoStack.Clear();
        }

        /// <summary>Removes the last drawing and replays the rest on the pristine copy. With nothing left
        /// to undo, the selection is cancelled (back to hover mode).</summary>
        void Undo()
        {
            if (mode != Mode.Selected) return;
            if (textEditing) CommitText();   // undo while typing discards the text being typed
            if (records.Count > 0 && pristineFront != null)
            {
                redoStack.Add(records[records.Count - 1]);
                records.RemoveAt(records.Count - 1);
                Replay();
                SnapshotCommitted();
                Invalidate(frontRect);
            }
            else ResetToHover();
        }

        /// <summary>Puts back the last undone drawing.</summary>
        void Redo()
        {
            if (mode != Mode.Selected || redoStack.Count == 0 || pristineFront == null) return;
            if (textEditing) CommitText();
            records.Add(redoStack[redoStack.Count - 1]);
            redoStack.RemoveAt(redoStack.Count - 1);
            Replay();
            SnapshotCommitted();
            Invalidate(frontRect);
        }

        /// <summary>Records are front-local: when the selection's corner moves, shifting them by the same
        /// amount the other way keeps every drawing over the same screen pixels.</summary>
        static void ShiftRecords(List<Record> list, int dx, int dy)
        {
            foreach (Record r in list)
            {
                r.P1.Offset(dx, dy);
                r.P2.Offset(dx, dy);
                if (r.Segments != null)
                    foreach (Point[] s in r.Segments)
                    {
                        s[0].Offset(dx, dy);
                        s[1].Offset(dx, dy);
                    }
            }
        }

        void Replay()
        {
            pristineFront.CopyTo(front.Hdc, 0, 0, front.Width, front.Height, 0, 0);
            IntPtr hdc = front.Hdc;
            foreach (Record r in records)
            {
                Record rec = r;
                if (rec.Kind == Tool.Text) { BurnText(rec.TextBox); continue; }
                if (rec.Kind == Tool.Pixelate) { Pixelate(front, NormalizedRect(rec.P1, rec.P2), rec.Width); continue; }
                if (rec.Kind == Tool.Arrow) { DrawArrow(hdc, rec.P1, rec.P2, rec.Width, rec.Color); continue; }
                Gdi.WithPen(hdc, rec.Width, rec.Color, delegate
                {
                    if (rec.Kind == Tool.Rectangle) Gdi.Rectangle(hdc, rec.P1.X, rec.P1.Y, rec.P2.X, rec.P2.Y);
                    else if (rec.Kind == Tool.Line) Gdi.Line(hdc, rec.P1.X, rec.P1.Y, rec.P2.X, rec.P2.Y);
                    else if (rec.Kind == Tool.Oval) Gdi.Ellipse(hdc, rec.P1.X, rec.P1.Y, rec.P2.X, rec.P2.Y);
                    else foreach (Point[] s in rec.Segments) Gdi.Line(hdc, s[0].X, s[0].Y, s[1].X, s[1].Y);
                });
            }
        }

        // ------------------------------------------------------------------------------------------

        /// <summary>Clears all annotation state (right-click reset and "undo with nothing left").</summary>
        void ResetTools()
        {
            DisposeTools();
            tool = Tool.None;
            drawMode = annotated = textEditing = textToolArmed = textToolSelected = mouseDrawing = false;
            for (int k = 0; k < panelToggle.Length; k++) panelToggle[k] = false;
            penWidth = 1;
            shapeColor = DefaultColor;
            handlesEnabled = true;
            SetToolButtonStates(Tool.None);
            SyncPenButtons();
            subPanel.Visible = false;
        }

        void DisposeTools()
        {
            foreach (AnnotationTextBox b in textBoxes) { Controls.Remove(b); b.Dispose(); }
            textBoxes.Clear();
            records.Clear();
            redoStack.Clear();
            currentText = null;
            if (pristineFront != null) { pristineFront.Dispose(); pristineFront = null; }
            if (committedFront != null) { committedFront.Dispose(); committedFront = null; }
        }
    }
}
