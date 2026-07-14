using System;

namespace SewerScan.Domain.Entities
{
    /// <summary>
    /// Represents elevation in metres.
    /// </summary>
    public sealed class Elevation
    {
        /// <summary>Elevation value in metres.</summary>
        public double Metres { get; }

        public Elevation(double metres)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres)) throw new ArgumentOutOfRangeException(nameof(metres));
            Metres = metres;
        }
    }
}
