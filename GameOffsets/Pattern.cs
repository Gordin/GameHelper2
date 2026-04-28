namespace GameOffsets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    public struct Pattern
    {
        /// <summary>
        ///     User Friendly name for the pattern.
        /// </summary>
        public readonly string Name;

        /// <summary>
        ///     Pattern bytes seperated by space or comma or both.
        ///     Each byte is considered as a HEX. Put ?? or ? in
        ///     case you don't care of that specific byte. e.g.
        ///     "0xD2 0xd2 d2, ??, f2". Put '^' in case you want the program
        ///     to calculate the BytesToSkip.
        /// </summary>
        public readonly byte[] Data;

        /// <summary>
        ///     Return true if the byte needs to be checked, otherwise
        ///     returns false (for a wildcard entry).
        /// </summary>
        public readonly bool[] Mask;

        /// <summary>
        ///     Number of bytes to skip in the offset returned from the pattern
        ///     in order to reach the static-address offset data. If the input HEX string
        ///     contains ^ in it, the BytesToSkip is calculated from the it.
        ///     e.g. "D2 F3 ^ 22 45 23" will put 2 in BytesToSkip.
        /// </summary>
        public readonly int BytesToSkip;

        /// <summary>
        ///     Parses the Array of bytes in HEX format and converts it into
        ///     a byte array and a mask (in bool format) array.
        /// </summary>
        /// <param name="arrayOfHexBytes">Array of bytes in HEX format.</param>
        /// <returns>byte array and a mask (bool) array for it.</returns>
        private static (byte[], bool[]) ParseArrayOfHexBytes(List<string> arrayOfHexBytes)
        {
            List<bool> mask = new();
            List<byte> data = new();
            for (var i = 0; i < arrayOfHexBytes.Count; i++)
            {
                var hexByte = arrayOfHexBytes[i];
                if (hexByte.StartsWith("?"))
                {
                    data.Add(0x00);
                    mask.Add(false);
                }
                else
                {
                    data.Add(byte.Parse(hexByte, NumberStyles.HexNumber));
                    mask.Add(true);
                }
            }

            return (data.ToArray(), mask.ToArray());
        }

        /// <summary>
        ///     Create a new instance of the Pattern.
        /// </summary>
        /// <param name="name">user friendly name for the pattern</param>
        /// <param name="arrayOfHexBytes">
        ///     Array of HEX Bytes with "^" in it to calculate the bytes to skip.
        /// </param>
        public Pattern(string name, string arrayOfHexBytes)
        {
            this.Name = name;
            var arrayOfHexBytesList = arrayOfHexBytes.Split(
                new[] { " ", "," }, StringSplitOptions.RemoveEmptyEntries).ToList();

            this.BytesToSkip = arrayOfHexBytesList.FindIndex("^".Equals);
            if (this.BytesToSkip < 0)
            {
                throw new ArgumentException(
                    $"Pattern '{name}' must contain a '^' marker to indicate BytesToSkip; use the (name, bytes, bytesToSkip) constructor for explicit values (audit F-006).",
                    nameof(arrayOfHexBytes));
            }

            (this.Data, this.Mask) = ParseArrayOfHexBytes(
                arrayOfHexBytesList.Where(hex => hex != "^").ToList());
        }

        /// <summary>
        ///     Create a new instance of the Pattern.
        /// </summary>
        /// <param name="name">user friendly name for the patter</param>
        /// <param name="arrayOfHexBytes">Array of HEX Bytes</param>
        /// <param name="bytesToSkip">
        ///     Number of bytes to skip to reach the static-address offset data.
        /// </param>
        public Pattern(string name, string arrayOfHexBytes, int bytesToSkip)
        {
            this.Name = name;
            this.BytesToSkip = bytesToSkip;
            (this.Data, this.Mask) = ParseArrayOfHexBytes(arrayOfHexBytes.Split(
                new[] { " ", "," }, StringSplitOptions.RemoveEmptyEntries).ToList());
        }

        /// <summary>
        ///     Pretty prints the Pattern.
        /// </summary>
        /// <returns>Pattern in string format.</returns>
        public override string ToString()
        {
            var data = $"Name: {this.Name} Pattern: ";
            for (var i = 0; i < this.Data.Length; i++)
            {
                if (this.Mask[i])
                {
                    data += $"0x{this.Data[i]:X} ";
                }
                else
                {
                    data += "?? ";
                }
            }

            data += $"BytesToSkip: {this.BytesToSkip}";
            return data;
        }
    }
}
