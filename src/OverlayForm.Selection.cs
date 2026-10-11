using System;
using System.Drawing;
using System.Windows.Forms;

namespace DisplayShot
{
    // Selection: drawing a new rectangle, moving it, resizing it with the 8 handles, the border and
    // the magnifier.
    internal sealed partial class OverlayForm
    {
        float magScale = 1f;         // DPI scale of the monitor under the cursor (magnifier size)
        int M(float v) { return (int)Math.Round(v * magScale); }

        bool selecting;              // left press in hover mode, button still down
        bool drewNew;                // a qualifying drag happened (cursor X and Y both differ from the anchor)
        Point anchor;
        Point dragPoint;             // last qualifying drag point (handles use signed sizes from the anchor)
        bool moving;                 // press inside an existing selection, button still down
        Point dragLast;
        int resizeMode;              // 1..8 while a handle is dragged (see HandleCursor for the order)
        int selL, selT, selR, selB;  // edges while resizing; may cross, the display is normalized
        DateTime lastPressMove;      // while pressed, marching ants appear when the mouse pauses
        bool handlesEnabled = true;
        bool subPanelBeforeResize;   // the drawing sub-panel steps aside during a handle drag

        GdiSurface front;            // the selected area: crop of the screenshot plus annotations

        bool magnifierVisible;
        Point magPoint;
        Rectangle magRect;

        // ------------------------------------------------------------------------------------------
        // Mouse

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point p = e.Location;
            if (mode == Mode.Selected && handlesEnabled && !selecting && !moving)
            {
                int h = HandleAt(p);
                if (h != 0)
                {
                    // Any mouse button drags a handle, also while a drawing tool is active.
                    if (textEditing) CommitText();
                    resizeMode = h;
                    selL = frontRect.Left; selT = frontRect.Top; selR = frontRect.Right; selB = frontRect.Bottom;
                    toolbar.Visible = false;
                    subPanelBeforeResize = subPanel.Visible;
                    subPanel.Visible = false;
                    return;
                }
            }
            if (e.Button == MouseButtons.Right) return;
            if (!frontRect.Contains(p)) return;   // pressing on the dimmed area does nothing

            if (ToolsMouseDown(e)) return;        // text tool / annotation mode
            if (mode == Mode.Hover) BeginSelection(p);
            else BeginMove(p);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point p = e.Location;
            if (resizeMode != 0) { ResizeTo(p); return; }
            if (selecting) { DragNew(p); return; }
            if (moving) { MoveTo(p); return; }
            if (ToolsMouseMove(e)) return;
            UpdateCursor(p);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Point p = e.Location;
            if (resizeMode != 0)
            {
                // End of a handle drag: show the toolbar again.
                resizeMode = 0;
                HideMagnifier();
                toolbar.Visible = true;
                PlaceToolbar(new Point(frontRect.Right, frontRect.Bottom));
                if (subPanelBeforeResize)
                {
                    subPanel.Visible = true;
                    PlaceSubPanel();
                }
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                // Right-click: exits in hover mode; with a selection, a right-click on the dimmed area goes
                // back to hover mode, and inside the selection it does nothing.
                if (mode == Mode.Hover && !selecting) Close();
                else if (mode == Mode.Selected && !frontRect.Contains(p) && !moving) ResetToHover();
                return;
            }
            if (ToolsMouseUp(e)) return;
            if (selecting) EndSelection();
            else if (moving) EndMove();
        }

        void UpdateCursor(Point p)
        {
            Cursor c = Cursors.Default;
            if (mode == Mode.Selected && handlesEnabled)
            {
                int h = HandleAt(p);
                if (h != 0) c = HandleCursor(h);
            }
            if (Cursor != c) Cursor = c;
        }

        // ------------------------------------------------------------------------------------------
        // New selection

        void BeginSelection(Point p)
        {
            handlesEnabled = true;
            toolbar.Visible = false;
            anchor = p;
            dragLast = p;
            selecting = true;
            drewNew = false;
            lastPressMove = DateTime.Now;
            Settings.SelectionHintDone = true;   // the first press retires the one-time hint for good
            CloseHint();
            InvalidateFrame(frontRect);
        }

        void DragNew(Point p)
        {
            if (p.X == anchor.X || p.Y == anchor.Y) return;   // both coordinates must differ: minimum 1x1
            lastPressMove = DateTime.Now;
            Rectangle old = frontRect;
            frontRect = Rectangle.FromLTRB(Math.Min(anchor.X, p.X), Math.Min(anchor.Y, p.Y),
                                           Math.Max(anchor.X, p.X), Math.Max(anchor.Y, p.Y));
            drewNew = true;
            dragPoint = p;
            InvalidateSelectionChange(old, frontRect);
            MoveMagnifier(p);
        }

        void EndSelection()
        {
            // Without a qualifying drag, frontRect is still the hovered window: click = select that window.
            selecting = false;
            mode = Mode.Selected;
            ReplaceFront();
            HideMagnifier();
            Invalidate(Rectangle.Inflate(frontRect, 10, 10));
            Point p = new Point(Math.Min(frontRect.Right, ClientSize.Width) - 1, frontRect.Bottom - 1);
            if (!ClientRectangle.Contains(p)) p = Point.Empty;
            PlaceToolbar(p);
            toolbar.Visible = true;
            Focus();
        }

        // ------------------------------------------------------------------------------------------
        // Moving the selection

        void BeginMove(Point p)
        {
            handlesEnabled = true;
            toolbar.Visible = false;
            moving = true;
            dragLast = p;
            lastPressMove = DateTime.Now;
        }

        void MoveTo(Point p)
        {
            lastPressMove = DateTime.Now;
            HideMagnifier();   // the magnifier is hidden while moving
            int dx = p.X - dragLast.X, dy = p.Y - dragLast.Y;
            Rectangle old = frontRect, r = frontRect;
            // Keep the selection on the desktop.
            if (r.Left + dx < 0) r.X = 0;
            else if (r.Right + dx > ClientSize.Width) r.X = ClientSize.Width - r.Width;
            else r.X += dx;
            if (r.Top + dy < 0) r.Y = 0;
            else if (r.Bottom + dy > ClientSize.Height) r.Y = ClientSize.Height - r.Height;
            else r.Y += dy;
            dragLast = p;
            if (r == old) return;
            frontRect = r;
            RefreshFront(old);
            InvalidateSelectionChange(old, frontRect);
        }

        void EndMove()
        {
            moving = false;
            InvalidateFrame(frontRect);
            Point p = new Point(Math.Min(frontRect.Right, ClientSize.Width) - 1, frontRect.Bottom - 1);
            if (!ClientRectangle.Contains(p)) p = Point.Empty;
            PlaceToolbar(p);
            toolbar.Visible = true;
            Focus();
        }

        // ------------------------------------------------------------------------------------------
        // Resizing with the handles

        void ResizeTo(Point p)
        {
            switch (resizeMode)
            {
                case 1: selL = p.X; selT = p.Y; break;
                case 2: selT = p.Y; break;
                case 3: selT = p.Y; selR = p.X; break;
                case 4: selR = p.X; break;
                case 5: selR = p.X; selB = p.Y; break;
                case 6: selB = p.Y; break;
                case 7: selL = p.X; selB = p.Y; break;
                case 8: selL = p.X; break;
            }
            Rectangle old = frontRect;
            frontRect = Rectangle.FromLTRB(Math.Min(selL, selR), Math.Min(selT, selB), Math.Max(selL, selR), Math.Max(selT, selB));
            RefreshFront(old);
            InvalidateSelectionChange(old, frontRect);
            MoveMagnifier(p);
        }

        /// <summary>1 top-left, 2 top-mid, 3 top-right, 4 right-mid, 5 bottom-right, 6 bottom-mid,
        /// 7 bottom-left, 8 left-mid. 0 = none.</summary>
        int HandleAt(Point p)
        {
            Rectangle[] hs = HandleRects(frontRect.X, frontRect.Y, frontRect.Width, frontRect.Height);
            for (int i = 0; i < hs.Length; i++) if (hs[i].Contains(p)) return i + 1;
            return 0;
        }

        static Cursor HandleCursor(int h)
        {
            switch (h)
            {
                case 1: case 5: return Cursors.SizeNWSE;
                case 3: case 7: return Cursors.SizeNESW;
                case 2: case 6: return Cursors.SizeNS;
                default: return Cursors.SizeWE;
            }
        }

        /// <summary>Handle positions, just inside the corners and edge midpoints. W and H are signed while
        /// drawing leftwards or upwards; L,T is then the anchor.</summary>
        Rectangle[] HandleRects(int L, int T, int W, int H)
        {
            int hs = Math.Max(6, S(6));   // 6 px at 100 %, larger on high-DPI monitors
            int hw = W / 2, hh = H / 2;
            int left = W >= 0 ? L + 1 : L - hs, right = W >= 0 ? L + W - 1 - hs : L + W;
            int top = H >= 0 ? T + 1 : T - hs, bottom = H >= 0 ? T + H - 1 - hs : T + H;
            int midX = L + hw - hs / 2, midY = T + hh - hs / 2;
            Point[] pts =
            {
                new Point(left, top), new Point(midX, top), new Point(right, top), new Point(right, midY),
                new Point(right, bottom), new Point(midX, bottom), new Point(left, bottom), new Point(left, midY)
            };
            Rectangle[] rs = new Rectangle[8];
            for (int i = 0; i < 8; i++) rs[i] = new Rectangle(pts[i], new Size(hs, hs));
            return rs;
        }

        /// <summary>Arrow keys: moves the selection 1 px (10 px with Shift).</summary>
        void Nudge(int dx, int dy)
        {
            if (mode != Mode.Selected || selecting || moving || resizeMode != 0 || mouseDrawing) return;
            if (textEditing) CommitText();
            Rectangle old = frontRect, r = frontRect;
            r.X = Math.Max(0, Math.Min(ClientSize.Width - r.Width, r.X + dx));
            r.Y = Math.Max(0, Math.Min(ClientSize.Height - r.Height, r.Y + dy));
            if (r == old) return;
            frontRect = r;
            RefreshFront(old);
            InvalidateSelectionChange(old, frontRect);
            Point p = new Point(Math.Min(frontRect.Right, ClientSize.Width) - 1, frontRect.Bottom - 1);
            if (!ClientRectangle.Contains(p)) p = Point.Empty;
            PlaceToolbar(p);
            if (subPanel.Visible) PlaceSubPanel();
        }

        // ------------------------------------------------------------------------------------------
        // Back to hover mode (right-click on the dimmed area)

        void ResetToHover()
        {
            ResetTools();
            mode = Mode.Hover;
            selecting = moving = false;
            resizeMode = 0;
            toolbar.Visible = false;
            HideMagnifier();
            if (front != null) { front.Dispose(); front = null; }
            hoverRect = frontRect = Rectangle.Empty;
            lastHoverPoint = new Point(int.MinValue, int.MinValue);
            Invalidate();
            HoverTick();
        }

        void ReplaceFront()
        {
            if (front != null) front.Dispose();
            front = frontRect.Width > 0 && frontRect.Height > 0 ? GdiSurface.Crop(pristine, frontRect) : null;
        }

        /// <summary>The selection was moved or resized: crop it again from the screenshot. Drawings keep
        /// their place on the screen: they shift by however far the selection's corner moved and are
        /// replayed on the new crop (parts outside the selection are simply cut off).</summary>
        void RefreshFront(Rectangle old)
        {
            ReplaceFront();
            if (!annotated || front == null) return;
            int dx = old.X - frontRect.X, dy = old.Y - frontRect.Y;
            if (dx != 0 || dy != 0)
            {
                ShiftRecords(records, dx, dy);
                ShiftRecords(redoStack, dx, dy);
            }
            if (pristineFront != null) pristineFront.Dispose();
            pristineFront = GdiSurface.Crop(front, new Rectangle(0, 0, front.Width, front.Height));
            Replay();
            SnapshotCommitted();
        }

        void InvalidateSelectionChange(Rectangle a, Rectangle b)
        {
            Rectangle u = Rectangle.Union(a.IsEmpty ? b : a, b.IsEmpty ? a : b);
            Invalidate(Rectangle.Inflate(u, 10, 10));
        }

        // ------------------------------------------------------------------------------------------
        // Border and handles

        void PaintSelectionFrame(IntPtr hdc)
        {
            Rectangle r = frontRect;
            bool pressed = selecting || moving;
            bool paused = pressed && (DateTime.Now - lastPressMove).TotalMilliseconds > AntsInterval;

            if (selecting && !drewNew)
            {
                PaintAnts(hdc, r);   // pressed on a window, no drag yet
                return;
            }
            Rectangle[] handles = selecting
                ? HandleRects(anchor.X, anchor.Y, dragPoint.X - anchor.X, dragPoint.Y - anchor.Y)
                : HandleRects(r.X, r.Y, r.Width, r.Height);
            if (paused)
            {
                PaintAnts(hdc, r);   // button held still: marching ants
            }
            else if (r.Width > 0 && r.Height > 0)
            {
                if (selecting)
                {
                    // Drawing a new selection: 1-px dashed line, light dashes on a dark base.
                    PaintDashedLine(hdc, r.Left, r.Top, r.Width, true);
                    PaintDashedLine(hdc, r.Left, r.Top, r.Height, false);
                    PaintDashedLine(hdc, r.Right - 1, r.Top, r.Height, false);
                    PaintDashedLine(hdc, r.Left, r.Bottom - 1, r.Width, true);
                }
                else
                {
                    // Finished selection: light outer line, dark inner line (visible on any content).
                    PaintFrameRect(hdc, r, LineLight);
                    if (r.Width > 2 && r.Height > 2) PaintFrameRect(hdc, Rectangle.Inflate(r, -1, -1), LineDark);
                }
            }
            foreach (Rectangle h in handles)
            {
                Gdi.FillRect(hdc, h, LineLight);
                Gdi.FillRect(hdc, Rectangle.Inflate(h, -1, -1), LineDark);
            }
        }

        static void PaintFrameRect(IntPtr hdc, Rectangle r, Color c)
        {
            Gdi.FillRect(hdc, new Rectangle(r.Left, r.Top, r.Width, 1), c);
            Gdi.FillRect(hdc, new Rectangle(r.Left, r.Bottom - 1, r.Width, 1), c);
            Gdi.FillRect(hdc, new Rectangle(r.Left, r.Top, 1, r.Height), c);
            Gdi.FillRect(hdc, new Rectangle(r.Right - 1, r.Top, 1, r.Height), c);
        }

        static void PaintDashedLine(IntPtr hdc, int x, int y, int len, bool horizontal)
        {
            Gdi.FillRect(hdc, horizontal ? new Rectangle(x, y, len, 1) : new Rectangle(x, y, 1, len), LineDark);
            for (int i = 0; i < len; i += 12)
            {
                int n = Math.Min(6, len - i);
                Gdi.FillRect(hdc, horizontal ? new Rectangle(x + i, y, n, 1) : new Rectangle(x, y + i, 1, n), LineLight);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Magnifier: 132x140, a 30x20 area zoomed to 120x90, with the selection size and the pixel colour

        void MoveMagnifier(Point p)
        {
            // Flips near the right/bottom edge of the monitor under the cursor.
            Rectangle mon = MonitorClientBounds(p);
            magScale = Native.MonitorScale(new Point(p.X + desktop.X, p.Y + desktop.Y));
            int left = p.X > mon.Right - M(112) ? p.X - M(132) : p.X + M(20);
            int top = p.Y > mon.Bottom - M(140) ? p.Y - M(160) : p.Y + M(20);
            Rectangle r = new Rectangle(left, top, M(132), M(140));
            if (magnifierVisible) Invalidate(magRect);
            magnifierVisible = true;
            magPoint = p;
            magRect = r;
            Invalidate(magRect);
        }

        void HideMagnifier()
        {
            if (!magnifierVisible) return;
            magnifierVisible = false;
            Invalidate(magRect);
        }

        void PaintMagnifier(IntPtr hdc)
        {
            Rectangle r = magRect;
            Gdi.FillRect(hdc, r, Theme.Background);
            PaintFrameRect(hdc, r, Theme.Charcoal);
            int ix = r.X + M(6), iy = r.Y + M(6), iw = M(120), ih = M(90), cx = ix + iw / 2, cy = iy + ih / 2;
            int save = Gdi.SaveDC(hdc);
            Gdi.IntersectClipRect(hdc, ix, iy, ix + iw, iy + ih);
            Gdi.FillRect(hdc, new Rectangle(ix, iy, iw, ih), Theme.Background);   // area outside the capture
            Gdi.SetStretchBltMode(hdc, Gdi.COLORONCOLOR);
            Gdi.StretchBlt(hdc, ix, iy, iw, ih, pristine.Hdc, magPoint.X - 15, magPoint.Y - 10, 30, 20, Native.SRCCOPY);
            // Crosshair through the cursor pixel: dark 3-px line with a light centre.
            Gdi.FillRect(hdc, new Rectangle(ix, cy - 1, iw, 3), LineDark);
            Gdi.FillRect(hdc, new Rectangle(cx - 1, iy, 3, ih), LineDark);
            Gdi.FillRect(hdc, new Rectangle(ix, cy, iw, 1), LineLight);
            Gdi.FillRect(hdc, new Rectangle(cx, iy, 1, ih), LineLight);
            Gdi.RestoreDC(hdc, save);
            PaintFrameRect(hdc, new Rectangle(ix - 1, iy - 1, iw + 2, ih + 2), Theme.Slate);
        }

        void PaintMagnifierLabels(Graphics g)
        {
            Rectangle r = magRect;
            Color c = pristine.GetPixel(magPoint.X, magPoint.Y);
            string rgb = c.IsEmpty ? "RGB \u2014" : "RGB " + c.R + ", " + c.G + ", " + c.B;
            TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            Font font = Theme.UiAt(magScale);
            TextRenderer.DrawText(g, frontRect.Width + " \u00D7 " + frontRect.Height, font,
                                  new Point(r.X + M(7), r.Y + M(103)), Theme.Lavender, Theme.Background, f);
            TextRenderer.DrawText(g, rgb, font, new Point(r.X + M(7), r.Y + M(120)), Theme.Muted, Theme.Background, f);
        }

        /// <summary>Bounds of the monitor containing a client point, in client coordinates.</summary>
        Rectangle MonitorClientBounds(Point clientPoint)
        {
            Rectangle m = Native.MonitorBounds(new Point(clientPoint.X + desktop.X, clientPoint.Y + desktop.Y));
            m.Offset(-desktop.X, -desktop.Y);
            return m;
        }
    }
}
