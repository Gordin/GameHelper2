namespace GameOffsets.Natives
{
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdTuple2D<T>
    {
        public T X;
        public T Y;

        public StdTuple2D(T x, T y)
        {
            this.X = x;
            this.Y = y;
        }

        public override string ToString()
        {
            return $"X: {this.X}, Y: {this.Y}";
        }
    }
}