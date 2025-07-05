using Sinos.Components;
using Sinos.Projects.Tracks;

namespace Sinos.Utils;

public static class EstimateUtil
{
    public static bool IsLowerMode(EstimateMode beforeMode, EstimateMode afterMode)
        => beforeMode < afterMode;

    public static IEnumerable<T> EnumerateLowerModePhrases<T>(this IEnumerable<T> source, EstimateMode mode)
        where T : INeutrinoPhrase
        => source.Where(x => x.EstimateMode == null || IsLowerMode(x.EstimateMode.Value, mode));
}
