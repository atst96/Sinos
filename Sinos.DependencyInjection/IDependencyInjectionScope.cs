using System;

namespace Sinos.DependencyInjection;

internal interface IDependencyInjectionScope
{
    /// <summary>対象インタフェース</summary>
    public Type? ImplementationFor { get => null; }
}
