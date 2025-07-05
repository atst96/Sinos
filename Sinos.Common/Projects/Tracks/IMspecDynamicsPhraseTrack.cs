using System.Numerics;

namespace Sinos.Projects.Tracks;

public interface IMspecDynamicsPhraseTrack<TPhrase, TNumber>
    where TPhrase : IMspecDynamicsPhrase<TNumber>
    where TNumber : IFloatingPointIeee754<TNumber>
{
    public TPhrase[] Phrases { get; }
}
