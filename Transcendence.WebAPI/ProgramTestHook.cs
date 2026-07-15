// Exposes the top-level-statement entry point as a public type so the integration test project can
// resolve WebApplicationFactory<Program>. Kept in its own file (never editing Program.cs) so this
// test-only hook stays independent of any in-flight Program.cs changes.
public partial class Program;
