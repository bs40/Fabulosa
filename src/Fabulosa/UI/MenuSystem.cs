using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Fabulosa.UI
{
    public sealed class MenuItem
    {
        public string Label;
        public Func<FrameContext, string> DynamicLabel;
        public Action<FrameContext> Action;
        public Func<FrameContext, bool> Checked;
        public Func<FrameContext, bool> Enabled;
        public bool IsSeparator;
        /// <summary>The menu stays open after this item is clicked, so several toggles can be flipped in a row.</summary>
        public bool KeepOpen;

        public static MenuItem Separator() => new MenuItem { IsSeparator = true };
        public string GetLabel(FrameContext f) => DynamicLabel != null ? DynamicLabel(f) : Label;
        public bool IsEnabled(FrameContext f) => Enabled == null || Enabled(f);
    }

    public sealed class Menu
    {
        public string Title;
        public Func<FrameContext, List<MenuItem>> Build;
    }

    /// <summary>Skia-drawn drop-down menus for the menu bar.</summary>
    public sealed class MenuSystem
    {
        private const float ItemH = 26f, PadX = 14f, MinWidth = 230f;
        private readonly List<Menu> _menus;
        private List<MenuItem> _items;
        private SKRect _dropRect;
        private int _hoverItem = -1;

        public MenuSystem(List<Menu> menus) { _menus = menus; }

        public int OpenIndex { get; private set; } = -1;
        public bool IsOpen => OpenIndex >= 0;

        public void Open(int index, SKRect anchor, FrameContext f)
        {
            OpenIndex = index;
            _items = _menus[index].Build(f);
            float width = MinWidth;
            using (var p = Theme.TextPaint(14f, SKColors.White, Theme.Sans))
                foreach (var it in _items)
                    if (!it.IsSeparator) width = Math.Max(width, p.MeasureText(it.GetLabel(f)) + PadX * 2 + 30);
            float height = 8;
            foreach (var it in _items) height += it.IsSeparator ? 9 : ItemH;
            float left = Math.Min(anchor.Left, Layout.Width - width - 14);
            _dropRect = new SKRect(left, 93, left + width, 93 + height);
            _hoverItem = -1;
        }

        public void Close() { OpenIndex = -1; _items = null; _hoverItem = -1; }

        public bool Contains(SKPoint p) => IsOpen && _dropRect.Contains(p);

        private int ItemAt(SKPoint p)
        {
            if (!IsOpen || !_dropRect.Contains(p)) return -1;
            float y = _dropRect.Top + 4;
            for (int i = 0; i < _items.Count; i++)
            {
                float h = _items[i].IsSeparator ? 9 : ItemH;
                if (p.Y >= y && p.Y < y + h) return _items[i].IsSeparator ? -1 : i;
                y += h;
            }
            return -1;
        }

        /// <summary>Centre of the open menu's item with this label, or null (used by the self-test).</summary>
        public SKPoint? ItemCentre(string label, FrameContext f)
        {
            if (!IsOpen) return null;
            float y = _dropRect.Top + 4;
            foreach (var it in _items)
            {
                float h = it.IsSeparator ? 9 : ItemH;
                if (!it.IsSeparator && it.GetLabel(f) == label) return new SKPoint(_dropRect.MidX, y + h / 2);
                y += h;
            }
            return null;
        }

        public void MouseMove(SKPoint p) { _hoverItem = ItemAt(p); }

        /// <summary>Returns true when the click was consumed by the menu.</summary>
        public bool MouseDown(SKPoint p, FrameContext f)
        {
            if (!IsOpen) return false;
            int i = ItemAt(p);
            if (i >= 0)
            {
                var item = _items[i];
                if (item.IsEnabled(f))
                {
                    if (!item.KeepOpen) Close();
                    try { item.Action?.Invoke(f); }
                    catch (Exception ex) { f.Host?.ShowMessage("Fabulosa", ex.Message); }
                }
                return true;
            }
            return _dropRect.Contains(p);
        }

        public void Draw(SKCanvas c, FrameContext f)
        {
            if (!IsOpen) return;
            using (var shadow = Theme.Glow(SKColors.Black, 6f, 160)) c.DrawRoundRect(SKRect.Create(_dropRect.Left, _dropRect.Top + 4, _dropRect.Width, _dropRect.Height), 5, 5, shadow);
            using (var bg = Theme.Fill(new SKColor(0x0f, 0x17, 0x18, 250))) c.DrawRoundRect(_dropRect, 5, 5, bg);
            using (var edge = Theme.Stroke(new SKColor(0x7a, 0x74, 0x5a), 1f)) c.DrawRoundRect(_dropRect, 5, 5, edge);

            float y = _dropRect.Top + 4;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.IsSeparator)
                {
                    using var sep = Theme.Fill(new SKColor(0x2a, 0x36, 0x37));
                    c.DrawRect(new SKRect(_dropRect.Left + 8, y + 4, _dropRect.Right - 8, y + 5), sep);
                    y += 9;
                    continue;
                }
                bool enabled = it.IsEnabled(f);
                var row = new SKRect(_dropRect.Left + 4, y, _dropRect.Right - 4, y + ItemH);
                if (i == _hoverItem && enabled)
                {
                    using var hl = Theme.Fill(new SKColor(0x21, 0x36, 0x3a));
                    c.DrawRoundRect(row, 3, 3, hl);
                }
                bool isChecked = it.Checked != null && it.Checked(f);
                if (isChecked)
                {
                    using var check = Theme.Stroke(Theme.CyanText, 2f);
                    check.StrokeCap = SKStrokeCap.Round;
                    float cx = _dropRect.Left + 16, cy = y + ItemH / 2;
                    c.DrawLine(cx - 5, cy, cx - 1, cy + 4, check);
                    c.DrawLine(cx - 1, cy + 4, cx + 6, cy - 4, check);
                }
                Theme.Text(c, it.GetLabel(f), _dropRect.Left + 30, y + ItemH / 2 + 5, 14f, enabled ? new SKColor(0xe4, 0xe4, 0xe4) : new SKColor(0x6e, 0x76, 0x76), Theme.Sans);
                y += ItemH;
            }
        }
    }
}
