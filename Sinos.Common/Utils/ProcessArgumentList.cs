using System.Numerics;

namespace Sinos.Utils;

public class ProcessArgumentList : List<string>
{
    public void Add(params ReadOnlySpan<string> values)
        => this.AddRange(values);

    public void Add<T>(string name, T value) where T : INumber<T>
        => this.AddRange(name, value.ToString()!);
}
