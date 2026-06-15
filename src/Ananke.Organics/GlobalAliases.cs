// Type aliases — these contracts were moved to Ananke.Abstractions to fix infrastructure
// dependency inversions (OTel/Qdrant should not depend on Ananke.Organics).
// Aliases preserve source-level compatibility for all Organics-internal consumers.
global using IBudgetMeter = Ananke.Abstractions.Budget.IBudgetMeter;
global using BudgetSpend = Ananke.Abstractions.Budget.BudgetSpend;
global using InMemoryBudgetMeter = Ananke.Abstractions.Budget.InMemoryBudgetMeter;
global using ChildSpec = Ananke.Abstractions.Agents.ChildSpec;
global using IDomainRouter = Ananke.Abstractions.Agents.IDomainRouter;
