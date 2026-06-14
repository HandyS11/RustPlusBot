using System.Reflection;

namespace RustPlusBot.Discord;

/// <summary>Registers an assembly whose Discord interaction modules should be loaded at startup.</summary>
/// <param name="Assembly">The assembly to scan for interaction modules.</param>
public sealed record InteractionModuleAssembly(Assembly Assembly);
