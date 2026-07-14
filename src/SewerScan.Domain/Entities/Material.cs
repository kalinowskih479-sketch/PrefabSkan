using System;

namespace SewerScan.Domain.Entities
{
    /// <summary>
    /// Represents a named material (entity wrapper) when additional metadata is required.
    /// </summary>
    public class Material
    {
        public Guid Id { get; }
        public string Name { get; }

        public Material(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Material name must be provided", nameof(name));
            Id = Guid.NewGuid();
            Name = name;
        }
    }
}
