using System.Windows.Forms;

namespace FilmEcho {
// A normal Panel cannot own keyboard focus. The native mpv child reports clicks
// over IPC so this host can become the active control in every container above it.
sealed class VideoSurface : Panel {
    public VideoSurface() { SetStyle(ControlStyles.Selectable,true); TabStop=true; }
    public void TakePlaybackFocus() { Select(); Focus(); }
}
}
