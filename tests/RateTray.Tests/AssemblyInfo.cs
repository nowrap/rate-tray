// Loc is process-wide state that several tests switch language on. Serialising the whole
// assembly is cheaper than threading a fixture through every class that formats a string.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
