using System;

namespace Sinos.DependencyInjection;

/// <summary>
/// Singletonで登録する
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class SingletonAttribute : Attribute, IDependencyInjectionScope { }
