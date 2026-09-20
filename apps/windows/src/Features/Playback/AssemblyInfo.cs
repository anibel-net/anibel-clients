using System.Runtime.CompilerServices;

// Lets PlaybackControllerTests exercise internal seams (engine wiring for
// local fakes) without reflection hacks. No production assemblies are exposed.
[assembly: InternalsVisibleTo("Anibel.App.Tests")]
