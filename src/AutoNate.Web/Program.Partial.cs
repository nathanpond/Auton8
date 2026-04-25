// Top-level statements emit an internal Program class. Declaring it as a public
// partial here lets WebApplicationFactory<Program> reference it from the test
// project without using InternalsVisibleTo.
public partial class Program;
