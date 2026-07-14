using System;
using System.Collections.Generic;

namespace SewerScan.Domain.Entities
{
    /// <summary>
    /// Strongly-typed identifier for a Project.
    /// </summary>
    public readonly record struct ProjectId(Guid Value)
    {
        /// <summary>Create a new ProjectId.</summary>
        public static ProjectId New() => new(Guid.NewGuid());
    }

    /// <summary>
    /// Represents a sewer scan project containing manholes and pipes.
    /// </summary>
    public class Project
    {
        /// <summary>Project identifier.</summary>
        public ProjectId Id { get; }

        /// <summary>Project display name.</summary>
        public string Name { get; }

        /// <summary>Manholes belonging to this project.</summary>
        public IReadOnlyList<Manhole> Manholes => _manholes.AsReadOnly();

        /// <summary>Pipes belonging to this project.</summary>
        public IReadOnlyList<Pipe> Pipes => _pipes.AsReadOnly();

        private readonly List<Manhole> _manholes = new();
        private readonly List<Pipe> _pipes = new();

        /// <summary>Create a new project.</summary>
        /// <exception cref="ArgumentException">Thrown when name is null or empty.</exception>
        public Project(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Project name must be provided", nameof(name));

            Id = ProjectId.New();
            Name = name;
        }

        /// <summary>Add a manhole to the project.</summary>
        public void AddManhole(Manhole manhole)
        {
            if (manhole is null) throw new ArgumentNullException(nameof(manhole));
            _manholes.Add(manhole);
        }

        /// <summary>Add a pipe to the project.</summary>
        public void AddPipe(Pipe pipe)
        {
            if (pipe is null) throw new ArgumentNullException(nameof(pipe));
            _pipes.Add(pipe);
        }
    }
}
