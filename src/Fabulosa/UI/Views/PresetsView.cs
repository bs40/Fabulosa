using System;
using Fabulosa.Model;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>Preset list with save / delete / import.</summary>
    public sealed class PresetsView : View
    {
        private const float ListTop = 425f, ItemH = 29f;
        private const int VisibleItems = 12;
        private static readonly SKRect ListRect = new SKRect(26, 423, 224, 425 + ItemH * VisibleItems);
        private static readonly SKRect[] Buttons = { new SKRect(32, 800, 84, 835), new SKRect(96, 800, 152, 835), new SKRect(162, 800, 218, 835) };
        private static readonly string[] ButtonLabels = { "SAVE", "DELETE", "IMPORT" };
        private int _scroll;
        private int _pressed = -1;

        public PresetsView() : base(Layout.Presets) { }

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "PRESETS");
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            var state = f.State;
            int count = state.Presets.Count;
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, count - VisibleItems));
            c.Save();
            c.ClipRect(ListRect);
            for (int row = 0; row < VisibleItems; row++)
            {
                int i = row + _scroll;
                if (i >= count) break;
                float top = ListTop + row * ItemH;
                bool selected = i == state.SelectedPreset;
                var r = new SKRect(28, top, 222, top + 27);
                if (selected)
                {
                    using var glow = Theme.Glow(new SKColor(0x2a, 0x7c, 0xf0), 3f, 90);
                    c.DrawRoundRect(r, 3, 3, glow);
                    using var fill = Theme.VerticalGradient(r, new SKColor(0x2f, 0x82, 0xf4), new SKColor(0x0c, 0x4e, 0xb8));
                    c.DrawRoundRect(r, 3, 3, fill);
                    using var edge = Theme.Stroke(new SKColor(0x62, 0xa8, 0xff), 1f);
                    c.DrawRoundRect(r, 3, 3, edge);
                }
                else if (f.Hover(r))
                {
                    using var hl = Theme.Fill(new SKColor(0x18, 0x22, 0x24));
                    c.DrawRoundRect(r, 3, 3, hl);
                }
                var col = selected ? SKColors.White : new SKColor(0xd6, 0xd6, 0xd6);
                Theme.Text(c, (i + 1).ToString("00"), 36f, top + 19f, 16f, col, Theme.Sans);
                string name = Theme.Ellipsize(state.Presets[i].Name, 150f, 16f);
                Theme.Text(c, name, 68f, top + 19f, 16f, col, Theme.Sans);
                if (selected && state.EqDirty)
                    Theme.Text(c, "•", 214f, top + 19f, 16f, new SKColor(0xff, 0xd0, 0x60), Theme.SansBold, SKTextAlign.Right);
            }
            c.Restore();

            if (count > VisibleItems)
            {
                // scrollbar
                float trackTop = ListRect.Top + 2, trackH = ListRect.Height - 4;
                float thumbH = Math.Max(20, trackH * VisibleItems / count);
                float thumbY = trackTop + (trackH - thumbH) * _scroll / Math.Max(1, count - VisibleItems);
                using var track = Theme.Fill(new SKColor(0x10, 0x16, 0x17));
                c.DrawRoundRect(new SKRect(226, trackTop, 230, trackTop + trackH), 2, 2, track);
                using var thumb = Theme.Fill(new SKColor(0x4a, 0x56, 0x58));
                c.DrawRoundRect(new SKRect(226, thumbY, 230, thumbY + thumbH), 2, 2, thumb);
            }

            for (int i = 0; i < 3; i++)
            {
                bool enabled = i != 1 || count > 1;
                Theme.Button(c, Buttons[i], enabled && f.Hover(Buttons[i]), _pressed == i && f.MouseDown);
                Theme.Text(c, ButtonLabels[i], Buttons[i].MidX, Buttons[i].MidY + 4.5f, 11.5f, enabled ? new SKColor(0xe0, 0xe0, 0xe0) : new SKColor(0x70, 0x76, 0x76), Theme.SansBold, SKTextAlign.Center, 0.6f);
            }
        }

        public override bool OnMouseDown(SKPoint p, FrameContext f)
        {
            var state = f.State;
            if (ListRect.Contains(p))
            {
                int row = (int)((p.Y - ListTop) / ItemH);
                int i = row + _scroll;
                if (row >= 0 && i >= 0 && i < state.Presets.Count)
                {
                    state.SelectPreset(i);
                    f.PersistSettings?.Invoke();
                }
                return true;
            }
            for (int i = 0; i < 3; i++)
            {
                if (!Buttons[i].Contains(p)) continue;
                _pressed = i;
                switch (i)
                {
                    case 0: SavePreset(f); break;
                    case 1: DeletePreset(f); break;
                    case 2: ImportPresets(f); break;
                }
                return true;
            }
            return false;
        }

        public override void OnMouseUp(SKPoint p, FrameContext f) { _pressed = -1; }

        public override bool OnWheel(SKPoint p, int delta, FrameContext f)
        {
            if (!ListRect.Contains(p)) return false;
            _scroll = Math.Clamp(_scroll - Math.Sign(delta), 0, Math.Max(0, f.State.Presets.Count - VisibleItems));
            return true;
        }

        public static void SavePreset(FrameContext f)
        {
            var state = f.State;
            if (state.Presets.Count == 0) { SaveAsNew(f); return; }
            var current = state.Presets[state.SelectedPreset];
            var snapshot = state.CaptureAsPreset(current.Name);
            state.Presets[state.SelectedPreset] = snapshot;
            state.EqDirty = false;
            f.PersistPresets?.Invoke();
            f.SetStatus?.Invoke($"Saved preset \"{current.Name}\"");
        }

        public static void SaveAsNew(FrameContext f)
        {
            var state = f.State;
            string name = f.Host?.PromptText("Save preset", "Preset name:", $"Preset {state.Presets.Count + 1}");
            if (string.IsNullOrWhiteSpace(name)) return;
            state.Presets.Add(state.CaptureAsPreset(name.Trim()));
            state.SelectedPreset = state.Presets.Count - 1;
            state.EqDirty = false;
            f.PersistPresets?.Invoke();
            f.PersistSettings?.Invoke();
        }

        public static void DeletePreset(FrameContext f)
        {
            var state = f.State;
            if (state.Presets.Count <= 1) return;
            string name = state.Presets[state.SelectedPreset].Name;
            state.Presets.RemoveAt(state.SelectedPreset);
            state.SelectedPreset = Math.Clamp(state.SelectedPreset, 0, state.Presets.Count - 1);
            state.ApplyPreset(state.Presets[state.SelectedPreset]);
            f.PersistPresets?.Invoke();
            f.PersistSettings?.Invoke();
            f.SetStatus?.Invoke($"Deleted preset \"{name}\"");
        }

        public static void ImportPresets(FrameContext f)
        {
            string path = f.Host?.OpenFileDialog("Import presets", "Fabulosa presets (*.json)|*.json|All files (*.*)|*.*");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var imported = Storage.ImportPresets(path);
                if (imported.Count == 0) { f.Host?.ShowMessage("Import presets", "No presets were found in that file."); return; }
                f.State.Presets.AddRange(imported);
                f.State.SelectedPreset = f.State.Presets.Count - imported.Count;
                f.State.ApplyPreset(f.State.Presets[f.State.SelectedPreset]);
                f.PersistPresets?.Invoke();
                f.PersistSettings?.Invoke();
                f.SetStatus?.Invoke($"Imported {imported.Count} preset(s)");
            }
            catch (Exception ex)
            {
                f.Host?.ShowMessage("Import presets", "Could not import presets:\n" + ex.Message);
            }
        }

        public static void ExportSelected(FrameContext f)
        {
            var state = f.State;
            if (state.Presets.Count == 0) return;
            var preset = state.CaptureAsPreset(state.Presets[state.SelectedPreset].Name);
            string path = f.Host?.SaveFileDialog("Export preset", "Fabulosa preset (*.json)|*.json", preset.Name + ".json");
            if (string.IsNullOrEmpty(path)) return;
            try { Storage.ExportPreset(path, preset); f.SetStatus?.Invoke($"Exported \"{preset.Name}\""); }
            catch (Exception ex) { f.Host?.ShowMessage("Export preset", "Could not export the preset:\n" + ex.Message); }
        }
    }
}
