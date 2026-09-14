using System;
using Fabulosa.Audio;
using Fabulosa.Media;
using Fabulosa.Model;
using SkiaSharp;

namespace Fabulosa.UI
{
    /// <summary>What the window can do on behalf of a view (window management + dialogs).</summary>
    public interface IHost
    {
        void DragWindow();
        void Minimize();
        void ToggleMaximize();
        void CloseWindow();
        void SetTopmost(bool on);
        void ResetWindowSize();
        bool IsMaximized { get; }
        string OpenFileDialog(string title, string filter);
        string SaveFileDialog(string title, string filter, string defaultName);
        void ShowMessage(string title, string text);
        void SaveScreenshot(string path);
        void RestartCapture();
        string PromptText(string title, string prompt, string initial);
    }

    /// <summary>Per-frame services handed to views.</summary>
    public sealed class FrameContext
    {
        public AppState State;
        public Analyzer Analyzer;
        public AudioCapture Capture;
        public MediaSessionService Media;
        public DancerClip DancerClip;
        public int DancerClipFrame = -1;   // clip frame drawn this frame, -1 while the stick figure shows
        public IHost Host;
        public SKPoint Mouse = new SKPoint(-1, -1);
        public bool MouseDown;
        public float Time;           // seconds since start
        public float Dt;
        public float Scale = 1f;     // design -> device pixels
        public bool MenuOpen;
        public Action<string> SetStatus;
        public Action MarkStaticDirty;
        public Action RearrangePanels;
        public Action PersistSettings;

        public bool Hover(SKRect r) => !MenuOpen && r.Contains(Mouse);
    }

    /// <summary>Base class for the panels. Static content is drawn once into a cached layer.</summary>
    public abstract class View
    {
        protected View() { }
        protected View(SKRect bounds) { Bounds = bounds; }

        public SKRect Bounds { get; private set; }

        /// <summary>The visualiser this panel is, for the View menu and the layout; null for the fixed parts of the rack.</summary>
        public virtual Visualiser? PanelId => null;

        /// <summary>Moves the view. Views that cache geometry rebuild it in <see cref="OnBoundsChanged"/>.</summary>
        public void SetBounds(SKRect bounds)
        {
            if (bounds == Bounds) return;
            Bounds = bounds;
            OnBoundsChanged();
        }

        protected virtual void OnBoundsChanged() { }

        public virtual void DrawStatic(SKCanvas c, FrameContext f) { }
        public virtual void Draw(SKCanvas c, FrameContext f) { }
        public virtual bool HitTest(SKPoint p) => Bounds.Contains(p);
        public virtual bool OnMouseDown(SKPoint p, FrameContext f) => false;
        public virtual void OnMouseMove(SKPoint p, FrameContext f) { }
        public virtual void OnMouseUp(SKPoint p, FrameContext f) { }
        public virtual bool OnDoubleClick(SKPoint p, FrameContext f) => false;
        public virtual bool OnWheel(SKPoint p, int delta, FrameContext f) => false;
        public virtual void OnSizeChanged(FrameContext f) { }
    }
}
