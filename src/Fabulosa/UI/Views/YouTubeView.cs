using System;
using Fabulosa.Model;
using SkiaSharp;

namespace Fabulosa.UI.Views
{
    /// <summary>
    /// The panel the embedded YouTube player sits in. Skia only draws the frame and whatever message belongs on the
    /// screen; the video itself is a WebView2 control the window positions over <see cref="Screen"/>, because a
    /// video window cannot be painted into the canvas.
    /// </summary>
    public sealed class YouTubeView : View
    {
        private static readonly SKColor Background = SKColors.Black;

        /// <summary>Message drawn on the panel; empty once the player covers it.</summary>
        public string Status { get; set; } = "";

        /// <summary>Where the window must put the player, in design coordinates.</summary>
        public SKRect Screen { get; private set; }

        protected override void OnBoundsChanged() => Screen = Layout.ScreenOf(Bounds);

        public override void DrawStatic(SKCanvas c, FrameContext f)
        {
            Theme.Panel(c, Bounds, "YOUTUBE");
            Theme.Screen(c, Screen, Background);
        }

        public override void Draw(SKCanvas c, FrameContext f)
        {
            if (string.IsNullOrEmpty(Status)) return;
            c.Save();
            c.ClipRoundRect(new SKRoundRect(Screen, 4, 4), SKClipOperation.Intersect, true);
            using (var fill = Theme.Fill(Background)) c.DrawRect(Screen, fill);
            float y = Screen.MidY - 8f;
            foreach (string line in Status.Split('\n'))
            {
                Theme.Text(c, line, Screen.MidX, y, 13f, Theme.TextDim, Theme.Sans, SKTextAlign.Center);
                y += 19f;
            }
            c.Restore();
        }

        /// <summary>
        /// Turns what the user pasted into what the player page needs: a watch, share, playlist or embed link, or a
        /// bare video or playlist id, becomes "v=.." and/or "list=..". Null when unrecognisable, so nothing else loads.
        /// </summary>
        public static string PlayerQuery(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string text = input.Trim();
            string video = null, list = null;

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                string host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host.Substring(4) : uri.Host;
                string path = uri.AbsolutePath.Trim('/');
                if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
                {
                    video = path;
                    list = Param(uri.Query, "list");
                }
                else if (host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase))
                {
                    if (path.StartsWith("embed/", StringComparison.OrdinalIgnoreCase)) video = path.Substring(6);
                    else if (path.Equals("watch", StringComparison.OrdinalIgnoreCase)) video = Param(uri.Query, "v");
                    else if (path.StartsWith("shorts/", StringComparison.OrdinalIgnoreCase)) video = path.Substring(7);
                    list = Param(uri.Query, "list");
                    if (video == "videoseries") video = null;
                }
                else return null;   // only YouTube addresses are ever loaded
            }
            else if (text.StartsWith("PL", StringComparison.Ordinal) || text.StartsWith("OL", StringComparison.Ordinal) ||
                     text.StartsWith("RD", StringComparison.Ordinal) || text.StartsWith("UU", StringComparison.Ordinal) ||
                     text.StartsWith("LL", StringComparison.Ordinal) || text.StartsWith("FL", StringComparison.Ordinal))
                list = text;
            else video = text;

            video = Clean(video);
            list = Clean(list);
            if (video != null && list != null) return $"v={video}&list={list}";
            if (video != null) return $"v={video}";
            if (list != null) return $"list={list}";
            return null;
        }

        /// <summary>Ids only ever contain these characters, so nothing from the clipboard can bend the address.</summary>
        private static string Clean(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return null;
            foreach (char ch in id)
                if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-') return null;
            return id;
        }

        private static string Param(string query, string key)
        {
            foreach (string part in query.TrimStart('?').Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && part.Substring(0, eq).Equals(key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(part.Substring(eq + 1));
            }
            return null;
        }
    }
}
