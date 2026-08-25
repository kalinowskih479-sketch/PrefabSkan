using System;
using System.Collections.Generic;

namespace SewerScan.Domain.Entities
{
    /// <summary>
    /// Identifier for a Manhole.
    /// </summary>
    public readonly record struct ManholeId(Guid Value)
    {
        public static ManholeId New() => new(Guid.NewGuid());
    }

    /// <summary>
    /// Types of manholes.
    /// </summary>
    public enum ManholeType
    {
        Circular,
        Rectangular,
        Other
    }

    /// <summary>
    /// Represents a manhole in the domain.
    /// </summary>
    public class Manhole
    {
        /// <summary>Manhole identifier.</summary>
        public ManholeId Id { get; }

        /// <summary>Location coordinate.</summary>
        public Coordinate Location { get; }

        /// <summary>Elevation of the manhole.</summary>
        public Elevation Elevation { get; }

        /// <summary>Type of the manhole.</summary>
        public ManholeType Type { get; }

        private readonly List<Inlet> _inlets = new();
        public IReadOnlyList<Inlet> Inlets => _inlets.AsReadOnly();

        /// <summary>Create a new manhole.</summary>
        public Manhole(Coordinate location, Elevation elevation, ManholeType type = ManholeType.Circular)
        {
            Location = location ?? throw new ArgumentNullException(nameof(location));
            Elevation = elevation ?? throw new ArgumentNullException(nameof(elevation));
            Id = ManholeId.New();
            Type = type;
        }

        /// <summary>Add an inlet to this manhole.</summary>
        public void AddInlet(Inlet inlet)
        {
            if (inlet is null) throw new ArgumentNullException(nameof(inlet));
            _inlets.Add(inlet);
        }
    }
}

