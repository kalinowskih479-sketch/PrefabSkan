using System;

namespace SewerScan.Domain.Entities
{
    /// <summary>
    /// Represents a 2D coordinate in project coordinate system.
    /// </summary>
    public sealed class Coordinate
    {
        /// <summary>X coordinate (easting).</summary>
        public double X { get; }

        /// <summary>Y coordinate (northing).</summary>
        public double Y { get; }

        public Coordinate(double x, double y)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentOutOfRangeException(nameof(x));
            if (double.IsNaN(y) || double.IsInfinity(y)) throw new ArgumentOutOfRangeException(nameof(y));
            X = x; Y = y;
        }
    }
}
