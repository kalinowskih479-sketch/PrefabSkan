using System;

namespace SewerScan.Domain.Entities
{
    /// <summary>
    /// Identifier for a PipeConnection.
    /// </summary>
    public readonly record struct PipeConnectionId(Guid Value)
    {
        public static PipeConnectionId New() => new(Guid.NewGuid());
    }

    /// <summary>
    /// Represents an endpoint of a pipe. It can reference a manhole, an inlet or be a free coordinate.
    /// </summary>
    public class PipeConnection
    {
        public PipeConnectionId Id { get; }

        public ManholeId? ManholeId { get; }
        public InletId? InletId { get; }
        public Coordinate? Location { get; }

        private PipeConnection(ManholeId? manholeId, InletId? inletId, Coordinate? location)
        {
            Id = PipeConnectionId.New();
            ManholeId = manholeId;
            InletId = inletId;
            Location = location;
        }

        /// <summary>Create a connection from a manhole.</summary>
        public static PipeConnection FromManhole(ManholeId manholeId) => new(manholeId, null, null);

        /// <summary>Create a connection from an inlet.</summary>
        public static PipeConnection FromInlet(InletId inletId) => new(null, inletId, null);

        /// <summary>Create a connection from a coordinate.</summary>
        public static PipeConnection FromCoordinate(Coordinate location)
        {
            if (location is null) throw new ArgumentNullException(nameof(location));
            return new(null, null, location);
        }
    }
}
