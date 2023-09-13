using System;

public static class VectorExtensions
{
    public static float Squared(this float x) => x * x;

    public static float Min(this float a, float b) => a < b ? a : b;

    public static float Max(this float a, float b) => a > b ? a : b;

    public static Vector3 Normal2D(this Vector3 v) => v.WithZ(0).Normal;

    public static float Length2D(this Vector3 v) => v.WithZ(0).Length;

    public static float Length2DSquared(this Vector3 v) => v.WithZ(0).LengthSquared;

    /// <summary>
    /// Project a vector onto a normal while maintaining its X/Y direction.
    /// Preserves magnitude by default.
    /// </summary>
    public static Vector3 ProjectZ(this Vector3 vector, Vector3 normal, bool rescale = true)
    {
        if (MathF.Abs(normal.z) <= 1e-8f)
            return Vector3.Zero;

        var projected = vector.WithZ((vector.x * normal.x + vector.y * normal.y) / -normal.z);
        return rescale ? projected.Normal * vector.Length : projected;
    }
}