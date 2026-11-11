using System;

namespace Platformer
{
    /// <summary>
    /// Marks a component whose object changes every frame (player, physics props).
    ///
    /// The region mirror copies objects carrying this attribute every frame. Everything
    /// else is static: it is only copied when the capture window is created, moved or
    /// resized.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class DynamicCaptureAttribute : Attribute
    {
    }
}
