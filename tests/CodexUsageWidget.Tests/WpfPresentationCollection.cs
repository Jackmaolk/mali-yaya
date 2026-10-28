using Xunit;

namespace CodexUsageWidget.Tests;

// WPF's shared BAML resource package cannot be read by concurrent STA test threads.
[CollectionDefinition("WpfPresentation", DisableParallelization = true)]
public sealed class WpfPresentationCollection;
