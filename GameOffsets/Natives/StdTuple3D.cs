namespace GameOffsets.Natives
{
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdTuple3D<T>
    {
        public T X;
        public T Y;
        public T Z;

        public override string ToString()
        {
            return $"X: {this.X}, Y: {this.Y}, Z: {this.Z}";
        }
    }
}