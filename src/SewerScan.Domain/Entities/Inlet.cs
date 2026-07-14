using System;

namespace SewerScan.Domain.Entities
{
    /// <summary>
    /// Identifier for an Inlet.
    /// </summary>
    public readonly record struct InletId(Guid Value)
    {
        public static InletId New() => new(Guid.NewGuid());
    }

    /// <summary>
    /// Represents an inlet connected to a manhole or pipe.
    /// </summary>
    public class Inlet
    {
        /// <summary>Inlet identifier.</summary>
        public InletId Id { get; }

        /// <summary>Location coordinate.</summary>
        public Coordinate Location { get; }

        /// <summary>Elevation of the inlet.</summary>
        public Elevation Elevation { get; }

        /// <summary>Indicates whether the inlet is active and inspected.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Create a new inlet.</summary>
        public Inlet(Coordinate location, Elevation elevation, bool isActive = true)
        {
            Location = location ?? throw new ArgumentNullException(nameof(location));
            Elevation = elevation ?? throw new ArgumentNullException(nameof(elevation));
            Id = InletId.New();
            IsActive = isActive;
        }

        /// <summary>Deactivate the inlet.</summary>
        public void Deactivate() => IsActive = false;
    }
}

