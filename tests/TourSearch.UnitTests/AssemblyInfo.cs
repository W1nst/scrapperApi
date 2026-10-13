using Microsoft.VisualStudio.TestTools.UnitTesting;

// Parallelization policy for this project — open question 12, ADR 036.
//
// Method level, because every test here is a pure in-memory assertion over the domain
// model or the EF model; they share no fixture, no database and no file state.
//
// The integration test project is a different case (one shared Testcontainers PostgreSQL
// instance) and keeps its own decision, so it still carries the interim suppression.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
