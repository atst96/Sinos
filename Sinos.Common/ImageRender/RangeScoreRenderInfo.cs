using Sinos.Constants;
using Sinos.Drawing;
using Sinos.Models.Scores;

namespace Sinos.ImageRender;

public class RangeScoreRenderInfo
{
    public required ScoreInfo Score { get; init; }

    public required IList<TimingHandle> Timings { get; init; }

    public required IList<VerticalLineInfo> NoteLines { get; init; }

    public required IList<VerticalLineInfo> RulerLines { get; init; }
}
