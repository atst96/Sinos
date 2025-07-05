using Sinos.Controls;
using Sinos.Controls.Editing;
using Sinos.Drawing;
using Sinos.Projects.Tracks;

namespace Sinos.ImageRender;

public class RenderInfoCommon
{
    public required INeutrinoTrack? Track { get; init; }

    public required RenderRangeInfo RenderRange { get; init; }

    public required EditorRenderLayout ScreenLayout { get; init; }

    public required ColorInfo ColorInfo { get; init; }

    public required RangeSelectingInfo? SelectionRange { get; set; }

    public RangeScoreRenderInfo? RangeScoreRenderInfo { get; set; }

    public int VScrollPosition { get; set; }

    internal void OnVerticalScrollChanged(int v)
    {
        this.VScrollPosition = v;
    }
}
