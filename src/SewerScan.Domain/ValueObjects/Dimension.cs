using System;

namespace SewerScan.Domain.Entities
{
    /// <summary>
    /// Represents a generic dimension (length) in metres.
    /// </summary>
    public sealed class Dimension
    {
        /// <summary>Length in metres.</summary>
        public double Metres { get; }

        public Dimension(double metres)
        {
            if (double.IsNaN(metres) || double.IsInfinity(metres) || metres <= 0) throw new ArgumentOutOfRangeException(nameof(metres));
            Metres = metres;
        }
    }
}
