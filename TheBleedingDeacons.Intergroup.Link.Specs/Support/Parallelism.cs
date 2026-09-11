// Scenarios run one at a time.
//
// MessageService announces an arrival through CommunityToolkit's
// WeakReferenceMessenger.Default, which is process-wide static state and
// has no per-test instance to hand a scenario. Left parallel, two
// scenarios delivering a push at once would each hear the other's
// announcement — and the scenario that matters most is the negative one,
// "a push that will not open announces nothing", which would then fail
// for a reason that has nothing to do with the code under test.
//
// The alternative is a seam over the messenger purely so the specs can
// isolate it, which is a production change made to suit a test host. This
// whole suite is a few hundred in-memory scenarios and a handful of small
// temporary files; serialising it costs a second or two.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
