using System;

namespace SewerScan.Domain.Entities
{
    /// <summary>
    /// Represents pipe diameter in millimetres as a value object.
    /// </summary>
    public sealed class Diameter
    {
        /// <summary>Diameter in millimetres.</summary>
        public double Millimetres { get; }

        /// <summary>Create a new Diameter value object.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when value is not positive.</exception>
        public Diameter(double millimetres)
        {
            if (double.IsNaN(millimetres) || millimetres <= 0) throw new ArgumentOutOfRangeException(nameof(millimetres), "Diameter must be a positive number.");
            Millimetres = millimetres;
        }
    }
}
