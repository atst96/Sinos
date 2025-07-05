using System.Numerics;
using Sinos.Projects.Tracks;

namespace Sinos.Renderers;

public interface IMgcDynamicsPhraseTrack<TPhrase, TNumber>
    where TPhrase : IMgcDynamicsPhrase<TNumber>
    where TNumber : IFloatingPointIeee754<TNumber>
{
    public TPhrase[] Phrases { get; }
}
