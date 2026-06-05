namespace GameOffsets.Objects.Components
{
    using System;
    using System.Runtime.InteropServices;
    using Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct LifeOffset
    {
        [FieldOffset(0x000)] public ComponentHeader Header;
        [FieldOffset(0x1b0)] public VitalStruct Health;
        [FieldOffset(0x208)] public VitalStruct Mana;
        [FieldOffset(0x248)] public VitalStruct EnergyShield;
        [FieldOffset(0x2E8)] public VitalStruct Ward;
        [FieldOffset(0x338)] public VitalStruct Divinity;
        [FieldOffset(0x380)] public StdVector SpiritPtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct VitalStruct
    {
        [FieldOffset(0x00)] public IntPtr VtablePtr;
        [FieldOffset(0x08)] public IntPtr PtrToLifeComponent;

        /// <summary>
        ///     e.g. Clarity reserve flat Vital
        /// </summary>
        [FieldOffset(0x10)] public int ReservedFlat;

        /// <summary>
        ///     e.g. Heralds reserve % Vital.
        ///     ReservedFlat does not change this value.
        ///     Note that it's an integer, this is due to 20.23% is stored as 2023
        /// </summary>
        [FieldOffset(0x14)] public int ReservedPercent;

        /// <summary>
        ///     This is greater than zero if Vital is regenerating
        ///     For value = 0 or less than 0, Vital isn't regenerating
        /// </summary>
        [FieldOffset(0x28)] public float Regeneration;
        [FieldOffset(0x2C)] public int Total;
        [FieldOffset(0x30)] public int Current;

        /// <summary>
        ///     Final Reserved amount of Vital after all the calculations.
        /// </summary>
        public int ReservedTotal => (int)Math.Ceiling(this.ReservedPercent / 10000f * this.Total) + this.ReservedFlat;

        /// <summary>
        ///     Final un-reserved amount of Vital after all the calculations.
        /// </summary>
        public int Unreserved => this.Total - this.ReservedTotal;

        /// <summary>
        ///     Returns current Vital in percentage (excluding the reserved vital) or returns zero in case the Vital
        ///     doesn't exists.
        /// </summary>
        /// <returns></returns>
        public int CurrentInPercent()
        {
            if (this.Total == 0)
            {
                return 0;
            }

            // Guard against full-reservation (e.g. Mana with multiple auras) - Unreserved
            // can be 0 or negative. Returning 0 mirrors the Total==0 fallback (audit F-023).
            var unreserved = this.Unreserved;
            if (unreserved <= 0)
            {
                return 0;
            }

            return (int)Math.Round(100d * this.Current / unreserved);
        }

        /// <summary>
        ///     Returns reserved Vital in percentage or returns zero in case the Vital doesn't exists.
        /// </summary>
        /// <returns></returns>
        public int ReservedInPercent()
        {
            if (this.Total == 0)
            {
                return 0;
            }

            return (int)Math.Round(100d * this.ReservedTotal / this.Total);
        }
    }
}
