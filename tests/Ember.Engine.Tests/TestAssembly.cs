using Xunit;

// Lifecycle tests pump worker-prepared results on their creating thread; parallel test bodies can starve those workers.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
