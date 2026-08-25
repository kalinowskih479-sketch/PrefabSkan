using System;

namespace SewerScan.Domain.Entities
{
    /// <summary>
    /// Identifier for a Pipe.
    /// </summary>
    public readonly record struct PipeId(Guid Value)
    {
        public static PipeId New() => new(Guid.NewGuid());
    }

    /// <summary>
    /// Materials available for pipes.
    /// </summary>
    public enum PipeMaterial
    {
        PVC,
        Concrete,
        Clay,
        Steel,
        PE
    }

    /// <summary>
    /// Direction of pipe flow or orientation.
    /// </summary>
    public enum PipeDirection
    {
        Unknown,
        Inbound,
        Outbound,
        Both
    }

    /// <summary>
    /// Represents a pipe segment connecting two endpoints.
    /// </summary>
    public class Pipe
    {
        public PipeId Id { get; }

        public PipeConnection Start { get; }
        public PipeConnection End { get; }

        public PipeMaterial Material { get; }

        public Diameter Diameter { get; }

        public PipeDirection Direction { get; }

        /// <summary>Create a new pipe.</summary>
        public Pipe(PipeConnection start, PipeConnection end, Diameter diameter, PipeMaterial material = PipeMaterial.Concrete, PipeDirection direction = PipeDirection.Unknown)
        {
            Start = start ?? throw new ArgumentNullException(nameof(start));
            End = end ?? throw new ArgumentNullException(nameof(end));
            Diameter = diameter ?? throw new ArgumentNullException(nameof(diameter));
            if (ReferenceEquals(start, end)) throw new ArgumentException("Start and end connections must be different", nameof(end));

            Material = material;
            Direction = direction;
            Id = PipeId.New();
        }
    }
}

